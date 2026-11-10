using GymForge.Application.Interfaces;
using GymForge.Application.Services;
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
    // set (no init): los valores persistidos por Company se aplican al arrancar.
    public decimal StopOnOweAmount { get; set; } = 10_000m;
    public decimal WarnOnOweAmount { get; set; } = 5_000m;
    public int AntiPassbackMinutes { get; set; } = 5;
}

public interface IValidateSwipeUseCase
{
    Task<AccessDecision> ValidateSwipeAsync(ValidateSwipeRequest request, CancellationToken ct = default);
}

public class ValidateSwipeUseCase : IValidateSwipeUseCase
{
    private readonly IMemberRepository _memberRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IChargeRepository _chargeRepo;
    private readonly IAccessLogRepository _accessLogRepo;
    private readonly IEventBus _eventBus;
    private readonly IClock _clock;
    private readonly ICompanyCalendar _calendar;
    private readonly ICurrentTenant _tenant;
    private readonly GatekeeperConfig _config;

    public ValidateSwipeUseCase(
        IMemberRepository memberRepo,
        IMembershipRepository membershipRepo,
        IChargeRepository chargeRepo,
        IAccessLogRepository accessLogRepo,
        IEventBus eventBus,
        IClock clock,
        ICompanyCalendar calendar,
        ICurrentTenant tenant,
        GatekeeperConfig config)
    {
        _memberRepo = memberRepo;
        _membershipRepo = membershipRepo;
        _chargeRepo = chargeRepo;
        _accessLogRepo = accessLogRepo;
        _eventBus = eventBus;
        _clock = clock;
        _calendar = calendar;
        _tenant = tenant;
        _config = config;
    }

    public async Task<AccessDecision> ValidateSwipeAsync(ValidateSwipeRequest request, CancellationToken ct = default)
    {
        // El tenant efectivo es el de la puerta: socio, membresía, deuda y anti-passback
        // se resuelven SOLO en esa empresa+sede (los query filters toman este scope).
        using var tenantScope = _tenant.BeginScope(request.CompanyId, request.SiteId);

        // 1. Resolver credencial → Member. La carga manual y el teclado son DNI; el lector de códigos
        //    prueba el tag del gimnasio y, si no hay, el DNI; el resto resuelve por tag físico.
        Member? member = request.Method switch
        {
            AccessMethod.KeypadPin or AccessMethod.Manual =>
                await _memberRepo.FindByDocumentAsync(DocumentType.DNI, request.Credential, request.CompanyId, ct),
            AccessMethod.Barcode =>
                await CredentialMatch.FindMemberAsync(_memberRepo, request.Credential, request.CompanyId, ct),
            _ => await _memberRepo.FindByTagSerialAsync(request.Credential, request.CompanyId, ct)
        };

        if (member is null)
        {
            await AppendDenied(request, null, AccessDenialReason.TagUnknown, ct);
            return Deny(Guid.Empty, string.Empty, null, AccessDenialReason.TagUnknown);
        }

        // 1b. Estado del socio (ortogonal a la membresía): allowlist fail-closed. Overdue entra: la deuda
        //     la resuelve StopOnOweAmount más abajo, no el estado.
        if (!CanEnter(member.Status))
        {
            var reason = member.Status == MemberStatus.Suspended
                ? AccessDenialReason.Suspended
                : AccessDenialReason.MemberInactive;
            await AppendDenied(request, member.Id, reason, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, reason);
        }

        // 2. Membresía activa
        var membership = await _membershipRepo.GetCurrentActiveAsync(member.Id, ct);
        if (membership is null)
        {
            await AppendDenied(request, member.Id, AccessDenialReason.IncompleteMembership, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.IncompleteMembership);
        }

        if (await IsPastEndDateAsync(membership, request.CompanyId, ct))
        {
            await AppendDenied(request, member.Id, AccessDenialReason.Expired, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.Expired);
        }

        // 3. Saldo pendiente
        var owing = await _chargeRepo.SumOutstandingAsync(member.Id, ct);
        if (owing > _config.StopOnOweAmount)
        {
            await AppendDenied(request, member.Id, AccessDenialReason.Owing, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.Owing, owing);
        }

        bool warn = owing > _config.WarnOnOweAmount;

        // 4. Horario permitido por el plan
        var localNow = await _calendar.LocalNowAsync(request.CompanyId, ct);

        if (!membership.MembershipType.IsTimeAllowed(localNow))
        {
            await AppendDenied(request, member.Id, AccessDenialReason.OutOfHours, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.OutOfHours, owing);
        }

        // 5. Puerta permitida por el plan
        if (membership.MembershipType.AllowedDoorIds.Count > 0 &&
            !membership.MembershipType.AllowedDoorIds.Contains(request.DoorId))
        {
            await AppendDenied(request, member.Id, AccessDenialReason.DoorNotAllowed, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.DoorNotAllowed, owing);
        }

        // 6. Anti-passback
        var last = await _accessLogRepo.GetLastGrantedAsync(member.Id, request.DoorId, ct);
        if (last is { Direction: AccessDirection.In } &&
            last.SwipedAt > _clock.Now.AddMinutes(-_config.AntiPassbackMinutes))
        {
            await AppendDenied(request, member.Id, AccessDenialReason.AlreadyInside, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.AlreadyInside, owing);
        }

        // 7. Visitas restantes (pack)
        if (membership.VisitsRemaining is 0)
        {
            await AppendDenied(request, member.Id, AccessDenialReason.NoVisitsLeft, ct);
            return Deny(member.Id, member.FullName, member.PhotoUrl, AccessDenialReason.NoVisitsLeft, owing);
        }

        // ALLOW — decrementar la visita ANTES de persistir: el SaveChanges de AppendAsync
        // (mismo DbContext scoped) confirma el log y el decremento en una sola transacción.
        if (membership.VisitsRemaining > 0)
        {
            membership.DecrementVisit();
            _membershipRepo.Update(membership);
        }

        var log = AccessLog.Granted(
            request.CompanyId, request.SiteId,
            member.Id, membership.Id,
            request.DoorId, request.Method,
            AccessDirection.In, request.Credential);

        await _accessLogRepo.AppendAsync(log, ct);

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

    /// <summary>
    /// La vigencia se compara contra el "hoy" local de la empresa, no el UTC: de 21:00 a 24:00 en
    /// Argentina la fecha UTC ya es la del día siguiente y el último día figuraría vencido.
    /// </summary>
    private async Task<bool> IsPastEndDateAsync(Membership membership, Guid companyId, CancellationToken ct)
    {
        if (membership.EndDate is null) return false;

        return membership.IsExpired(await _calendar.TodayAsync(companyId, ct));
    }

    private static bool CanEnter(MemberStatus status) =>
        status is MemberStatus.Active or MemberStatus.Overdue;

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
        AccessDenialReason reason,
        CancellationToken ct)
    {
        var log = memberId.HasValue
            ? AccessLog.Denied(
                request.CompanyId, request.SiteId,
                memberId.Value, request.DoorId,
                request.Method, reason,
                request.Credential)
            : AccessLog.DeniedUnknown(
                request.CompanyId, request.SiteId,
                request.DoorId, request.Method, reason,
                request.Credential);

        await _accessLogRepo.AppendAsync(log, ct);
    }
}
