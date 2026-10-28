using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests;

public class GatekeeperTests
{
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();
    private readonly IAccessLogRepository _accessLogRepo = Substitute.For<IAccessLogRepository>();
    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly FakeClock _clock = new(DateTime.UtcNow);
    private readonly ISiteRepository _siteRepo = Substitute.For<ISiteRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IDisposable _tenantScope = Substitute.For<IDisposable>();

    private readonly GatekeeperConfig _config = new()
    {
        StopOnOweAmount = 10_000m,
        WarnOnOweAmount = 5_000m,
        Timezone = "UTC",
        AntiPassbackMinutes = 5
    };

    public GatekeeperTests()
    {
        _tenant.BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_tenantScope);
        GivenCompanyIn("Argentina Standard Time");
    }

    private ValidateSwipeUseCase CreateSut() => new(
        _memberRepo, _membershipRepo, _chargeRepo, _accessLogRepo, _eventBus, _clock,
        new CompanyCalendar(_clock, _siteRepo, NullLogger<CompanyCalendar>.Instance), _tenant, _config);

    private void GivenCompanyIn(string timezone) =>
        _siteRepo.GetCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Company.Create("Gym Test", "30-12345678-9", timezone));

    private static Member MakeActiveMember()
    {
        var m = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Juan", "Pérez",
            DocumentType.DNI, "12345678", Gender.Male);
        m.Activate(DateOnly.FromDateTime(DateTime.Today));
        return m;
    }

    private static Membership MakeActiveMembership(
        Guid memberId, DateOnly? start = null, DateOnly? end = null, int? visitsRemaining = null) =>
        BuildActiveMembership(
            memberId,
            start ?? DateOnly.FromDateTime(DateTime.Today),
            end ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(1)),
            visitsRemaining);

    private static Membership MakeOpenEndedMembership(Guid memberId) =>
        BuildActiveMembership(memberId, DateOnly.FromDateTime(DateTime.Today), end: null, visitsRemaining: null);

    private static Membership BuildActiveMembership(
        Guid memberId, DateOnly start, DateOnly? end, int? visitsRemaining)
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), memberId, Guid.NewGuid(), start, end, visitsRemaining);
        ms.Activate();

        // El Gatekeeper desreferencia MembershipType (pasos 4 y 5). En producción viene
        // cargado por el repositorio (Include); acá lo adjuntamos a la navegación de solo-lectura.
        var type = MembershipType.Create(Guid.NewGuid(), "Full Access", MembershipBasis.OpenEnded, 30_000m);
        typeof(Membership).GetProperty(nameof(Membership.MembershipType))!.SetValue(ms, type);
        return ms;
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HappyPath_ValidRfidSwipe_GrantsAccess()
    {
        var now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.Now = now;

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByTagSerialAsync("TAG001", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);
        _accessLogRepo.GetLastGrantedAsync(member.Id, 1, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut();
        var result = await sut.ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeTrue();
        result.MemberFullName.Should().Be("Juan Pérez");
        result.HasDebtWarning.Should().BeFalse();

        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a => a.AccessGranted && a.Direction == AccessDirection.In),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManualEntry_ResolvesByDni_NotByTag()
    {
        var now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.Now = now;

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByDocumentAsync(DocumentType.DNI, "12345678", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);
        _accessLogRepo.GetLastGrantedAsync(member.Id, 1, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("12345678", AccessMethod.Manual, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeTrue();
        result.MemberFullName.Should().Be("Juan Pérez");
        await _memberRepo.DidNotReceive()
            .FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    // ── Tenant de la puerta ───────────────────────────────────────────────────

    [Fact]
    public async Task Swipe_OpensTheTenantScopeOfTheDoor_AndClosesItWhenDone()
    {
        var member = MakeActiveMember();
        ArrangeGrantableSwipe(member, MakeActiveMembership(member.Id));
        var request = TagSwipe();

        await CreateSut().ValidateSwipeAsync(request);

        _tenant.Received(1).BeginScope(request.CompanyId, request.SiteId);
        _tenantScope.Received(1).Dispose();
    }

    [Fact]
    public async Task Swipe_ResolvesTheMemberInsideTheTenantScope()
    {
        var member = MakeActiveMember();
        ArrangeGrantableSwipe(member, MakeActiveMembership(member.Id));

        await CreateSut().ValidateSwipeAsync(TagSwipe());

        Received.InOrder(() =>
        {
            _tenant.BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>());
            _memberRepo.FindByTagSerialAsync("TAG001", Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Swipe_ClosesTheTenantScope_EvenWhenTheDecisionThrows()
    {
        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns<Member?>(_ => throw new InvalidOperationException("db caída"));

        var act = () => CreateSut().ValidateSwipeAsync(TagSwipe());

        await act.Should().ThrowAsync<InvalidOperationException>();
        _tenantScope.Received(1).Dispose();
    }

    // ── Visit packs ───────────────────────────────────────────────────────────

    /// <summary>Deja todo listo para que el swipe del socio sea concedido (sin deuda ni passback).</summary>
    private void ArrangeGrantableSwipe(Member member, Membership membership, DateTime? utcNow = null)
    {
        _clock.Now = utcNow ?? new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);

        _memberRepo.FindByTagSerialAsync("TAG001", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);
        _accessLogRepo.GetLastGrantedAsync(member.Id, 1, Arg.Any<CancellationToken>())
            .ReturnsNull();
    }

    private static ValidateSwipeRequest TagSwipe() =>
        new("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public async Task VisitPack_GrantedSwipe_DecrementsVisitOnce()
    {
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id, visitsRemaining: 5);
        ArrangeGrantableSwipe(member, membership);

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeTrue();
        membership.VisitsRemaining.Should().Be(4);
        _membershipRepo.Received(1).Update(Arg.Is<Membership>(m => m.VisitsRemaining == 4));
    }

    [Fact]
    public async Task VisitPack_GrantedSwipe_MarksMembershipBeforeAppendingLog()
    {
        // AppendAsync hace el SaveChanges: el decremento tiene que estar marcado antes,
        // así log y visita se confirman en la misma transacción.
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id, visitsRemaining: 5);
        ArrangeGrantableSwipe(member, membership);

        await CreateSut().ValidateSwipeAsync(TagSwipe());

        Received.InOrder(() =>
        {
            _membershipRepo.Update(Arg.Any<Membership>());
            _accessLogRepo.AppendAsync(Arg.Any<AccessLog>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task VisitPack_LastVisit_IsGrantedAndLeavesZero()
    {
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id, visitsRemaining: 1);
        ArrangeGrantableSwipe(member, membership);

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeTrue();
        membership.VisitsRemaining.Should().Be(0);
        _membershipRepo.Received(1).Update(Arg.Is<Membership>(m => m.VisitsRemaining == 0));
    }

    [Fact]
    public async Task VisitPack_Exhausted_DeniesWithNoVisitsLeftAndDoesNotUpdate()
    {
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id, visitsRemaining: 0);
        ArrangeGrantableSwipe(member, membership);

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.NoVisitsLeft);
        _membershipRepo.DidNotReceive().Update(Arg.Any<Membership>());
    }

    [Fact]
    public async Task OpenEndedMembership_GrantedSwipe_DoesNotUpdateMembership()
    {
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);
        ArrangeGrantableSwipe(member, membership);

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeTrue();
        _membershipRepo.DidNotReceive().Update(Arg.Any<Membership>());
    }

    // ── Denial scenarios ──────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownTag_DeniesWithTagUnknown()
    {
        _clock.Now = DateTime.UtcNow;
        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("UNKNOWN", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
    }

    [Fact]
    public async Task UnknownTag_WritesDeniedLog()
    {
        _clock.Now = DateTime.UtcNow;
        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();
        var request = new ValidateSwipeRequest("UNKNOWN", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid());

        await CreateSut().ValidateSwipeAsync(request);

        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a =>
                !a.AccessGranted &&
                a.MemberId == null &&
                a.DenialReason == AccessDenialReason.TagUnknown &&
                a.TagSerial == "UNKNOWN" &&
                a.CompanyId == request.CompanyId &&
                a.SiteId == request.SiteId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoActiveMembership_DeniesWithIncompleteMembership()
    {
        _clock.Now = DateTime.UtcNow;
        var member = MakeActiveMember();

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.IncompleteMembership);
    }

    [Fact]
    public async Task MemberOwesAboveThreshold_DeniesWithOwing()
    {
        var now = DateTime.UtcNow;
        _clock.Now = now;

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(15_000m);  // > StopOnOweAmount (10_000)

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Owing);
    }

    [Fact]
    public async Task AntiPassback_SameDirection_DeniesAlreadyInside()
    {
        var now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.Now = now;

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);
        var lastLog = AccessLog.Granted(
            Guid.NewGuid(), Guid.NewGuid(), member.Id, membership.Id,
            1, AccessMethod.RfidCard, AccessDirection.In);

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);
        _accessLogRepo.GetLastGrantedAsync(member.Id, 1, Arg.Any<CancellationToken>())
            .Returns(lastLog);  // Entered 0 min ago → within AntiPassbackMinutes=5

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.AlreadyInside);
    }

    [Fact]
    public async Task DebtWarning_BelowStopThreshold_GrantsWithWarningFlag()
    {
        var now = DateTime.UtcNow;
        _clock.Now = now;

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(7_000m);  // > WarnOnOweAmount (5_000) but < StopOnOweAmount (10_000)
        _accessLogRepo.GetLastGrantedAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeTrue();
        result.HasDebtWarning.Should().BeTrue();
        result.OutstandingAmount.Should().Be(7_000m);
    }

    [Fact]
    public async Task ExpiredMembership_DeniesWithExpired()
    {
        var now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        _clock.Now = now;

        var member = MakeActiveMember();

        // Membresía vencida ayer, relativo al reloj mockeado (no a DateTime.Today real)
        var ms = MakeActiveMembership(member.Id,
            start: DateOnly.FromDateTime(now).AddMonths(-2),
            end: DateOnly.FromDateTime(now).AddDays(-1));

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(ms);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Expired);
    }

    // ── Vigencia contra la fecha LOCAL de la empresa (no la UTC) ──────────────

    private static readonly DateOnly LastValidDay = new(2026, 4, 21);

    private async Task<AccessDecision> SwipeWithMembershipEndingOnLastValidDayAtAsync(DateTime utcNow)
    {
        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id, start: LastValidDay.AddMonths(-1), end: LastValidDay);
        ArrangeGrantableSwipe(member, membership, utcNow);

        return await CreateSut().ValidateSwipeAsync(TagSwipe());
    }

    [Fact]
    public async Task LastDayOfValidity_At2200Argentina_IsGranted()
    {
        // 22:00 del 21/04 en Argentina = 01:00 UTC del 22/04: con la fecha UTC figuraría vencida.
        var result = await SwipeWithMembershipEndingOnLastValidDayAtAsync(
            new DateTime(2026, 4, 22, 1, 0, 0, DateTimeKind.Utc));

        result.Granted.Should().BeTrue();
        result.DenialReason.Should().BeNull();
    }

    [Fact]
    public async Task LastDayOfValidity_OneMinuteBeforeLocalMidnight_IsGranted()
    {
        var result = await SwipeWithMembershipEndingOnLastValidDayAtAsync(
            new DateTime(2026, 4, 22, 2, 59, 0, DateTimeKind.Utc));

        result.Granted.Should().BeTrue();
    }

    [Fact]
    public async Task DayAfterTheLastDay_AtLocalMidnight_IsDeniedAsExpired()
    {
        var result = await SwipeWithMembershipEndingOnLastValidDayAtAsync(
            new DateTime(2026, 4, 22, 3, 0, 0, DateTimeKind.Utc));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Expired);
    }

    [Fact]
    public async Task DayAfterTheLastDay_DuringTheDay_IsDeniedAsExpired()
    {
        var result = await SwipeWithMembershipEndingOnLastValidDayAtAsync(
            new DateTime(2026, 4, 22, 15, 0, 0, DateTimeKind.Utc));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Expired);
    }

    [Fact]
    public async Task Vigencia_UsesTheZoneOfTheCompany_NotAFixedOne()
    {
        // 20:00 UTC del 21/04 = 08:00 del 22/04 en Nueva Zelanda: ahí la membresía ya venció.
        GivenCompanyIn("New Zealand Standard Time");

        var result = await SwipeWithMembershipEndingOnLastValidDayAtAsync(
            new DateTime(2026, 4, 21, 20, 0, 0, DateTimeKind.Utc));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.Expired);
    }

    [Fact]
    public async Task OpenEndedMembership_NeverExpires_AndDoesNotNeedTheCompany()
    {
        var member = MakeActiveMember();
        ArrangeGrantableSwipe(member, MakeOpenEndedMembership(member.Id));

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeTrue();
        await _siteRepo.DidNotReceive().GetCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
