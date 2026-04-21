using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.Events;

namespace GymForge.Application.UseCases.Access;

public sealed record AccessDecision(
    bool Granted,
    Guid MemberId,
    string MemberFullName,
    string? PhotoUrl,
    AccessDenialReason? DenialReason,
    bool HasDebtWarning,
    decimal OutstandingAmount,
    MembershipStatus? MembershipStatus);

public sealed record ValidateSwipeRequest(
    string Credential,
    AccessMethod Method,
    int DoorId,
    Guid SiteId,
    Guid CompanyId);

public class GatekeeperConfig
{
    public decimal StopOnOweAmount { get; init; } = 10_000m;
    public decimal WarnOnOweAmount { get; init; } = 5_000m;
    public string Timezone { get; init; } = "Argentina Standard Time";
    public int AntiPassbackMinutes { get; init; } = 5;
}

public class ValidateSwipeUseCase
{
    private readonly IMemberRepository _memberRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IChargeRepository _chargeRepo;
    private readonly IAccessLogRepository _accessLogRepo;
    private readonly IEventBus _eventBus;
    private readonly IClock _clock;
    private readonly GatekeeperConfig _config;

    public ValidateSwipeUseCase(
        IMemberRepository memberRepo,
        IMembershipRepository membershipRepo,
        IChargeRepository chargeRepo,
        IAccessLogRepository accessLogRepo,
        IEventBus eventBus,
        IClock clock,
        GatekeeperConfig config)
    {
        _memberRepo = memberRepo;
        _membershipRepo = membershipRepo;
        _chargeRepo = chargeRepo;
        _accessLogRepo = accessLogRepo;
        _eventBus = eventBus;
        _clock = clock;
        _config = config;
    }

    public async Task<AccessDecision> ValidateSwipeAsync(ValidateSwipeRequest request, CancellationToken ct = default)
    {
        // 1. Resolver credencial → Member
        Member? member = request.Method switch
        {
            AccessMethod.RfidCard => await _memberRepo.FindByTagSerialAsync(request.Credential, request.CompanyId, ct),
            AccessMethod.KeypadPin => await _memberRepo.FindByDocumentAsync(DocumentType.DNI, request.Credential, request.CompanyId, ct),
            _ => await _memberRepo.FindByTagSerialAsync(request.Credential, request.CompanyId, ct)
        };

        if (member is null)
        {
            await AppendDenied(request, null, null, AccessDenialReason.TagUnknown, ct);
            return Deny(Guid.Empty, string.Empty, null, AccessDenialReason.TagUnknown);
        }

        // 2. Membresía activa
        var membership = await _membershipRepo.GetCurrentActiveAsync(member.Id, ct);
        if (membership is null)
        {
            await AppendDenied(request, member.Id, null, AccessDenialReason.IncompleteMembership, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.IncompleteMembership);
        }

        if (membership.EndDate.HasValue && membership.EndDate.Value < _clock.Today)
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.Expired, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.Expired);
        }

        // 3. Saldo pendiente
        var owing = await _chargeRepo.SumOutstandingAsync(member.Id, ct);
        if (owing > _config.StopOnOweAmount)
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.Owing, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.Owing, owing);
        }

        bool warn = owing > _config.WarnOnOweAmount;

        // 4. Horario permitido por el plan
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(
            _clock.Now,
            TimeZoneInfo.FindSystemTimeZoneById(_config.Timezone));

        if (!membership.MembershipType.IsTimeAllowed(localNow, _config.Timezone))
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.OutOfHours, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.OutOfHours, owing);
        }

        // 5. Puerta permitida por el plan
        if (membership.MembershipType.AllowedDoorIds.Count > 0 &&
            !membership.MembershipType.AllowedDoorIds.Contains(request.DoorId))
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.DoorNotAllowed, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.DoorNotAllowed, owing);
        }

        // 6. Anti-passback
        var last = await _accessLogRepo.GetLastAsync(member.Id, request.DoorId, ct);
        if (last is { Direction: AccessDirection.In } &&
            last.SwipedAt > _clock.Now.AddMinutes(-_config.AntiPassbackMinutes))
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.AlreadyInside, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.AlreadyInside, owing);
        }

        // 7. Visitas restantes (pack)
        if (membership.VisitsRemaining is 0)
        {
            await AppendDenied(request, member.Id, membership.Id, AccessDenialReason.NoVisitsLeft, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.NoVisitsLeft, owing);
        }

        // ALLOW — escribir log y publicar evento
        var log = AccessLog.Granted(
            request.CompanyId, request.SiteId,
            member.Id, membership.Id,
            request.DoorId, request.Method,
            AccessDirection.In, request.Credential);

        await _accessLogRepo.AppendAsync(log, ct);

        if (membership.VisitsRemaining > 0)
            membership.DecrementVisit();

        await _eventBus.PublishAsync(new MemberCheckedIn(member.Id, warn, owing), ct);

        return new AccessDecision(
            Granted: true,
            MemberId: member.Id,
            MemberFullName: member.FullName,
            PhotoUrl: member.PhotoUrl,
            DenialReason: null,
            HasDebtWarning: warn,
            OutstandingAmount: owing,
            MembershipStatus: membership.Status);
    }

    private static AccessDecision Deny(
        Guid memberId,
        string fullName,
        string? photoUrl,
        AccessDenialReason reason,
        decimal owing = 0) =>
        new(false, memberId, fullName, photoUrl, reason, false, owing, null);

    private async Task AppendDenied(
        ValidateSwipeRequest request,
        Guid? memberId,
        Guid? membershipId,
        AccessDenialReason reason,
        CancellationToken ct)
    {
        if (!memberId.HasValue) return;

        var log = AccessLog.Denied(
            request.CompanyId, request.SiteId,
            memberId.Value, request.DoorId,
            request.Method, reason,
            request.Credential);

        await _accessLogRepo.AppendAsync(log, ct);
    }
}
