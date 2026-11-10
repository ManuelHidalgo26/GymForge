using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.Events;
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

    // ── Estado del socio: allowlist {Active, Overdue}, corta antes de cualquier consulta de membresía ──

    private async Task<AccessDecision> SwipeAsync(Member member, Membership? membership)
    {
        _clock.Now = new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc);
        _memberRepo.FindByTagSerialAsync("TAG001", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>()).Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>()).Returns(0m);
        _accessLogRepo.GetLastGrantedAsync(member.Id, 1, Arg.Any<CancellationToken>()).ReturnsNull();

        return await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("TAG001", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));
    }

    private async Task AssertDeniedByMemberStatusAsync(
        Member member, AccessDenialReason reason, Membership? membership)
    {
        var result = await SwipeAsync(member, membership);

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(reason);
        result.MemberId.Should().Be(member.Id);
        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a => !a.AccessGranted && a.MemberId == member.Id && a.DenialReason == reason),
            Arg.Any<CancellationToken>());
        _membershipRepo.ReceivedCalls().Should().BeEmpty("el estado del socio corta antes de mirar la membresía");
        await _eventBus.DidNotReceiveWithAnyArgs().PublishAsync<MemberCheckedIn>(default!);
    }

    [Fact]
    public async Task SuspendedMember_WithActiveMembership_IsDeniedAsSuspended_WithoutConsumingAVisit()
    {
        var member = MakeActiveMember();
        member.Suspend();
        var pack = MakeActiveMembership(member.Id, visitsRemaining: 5);

        await AssertDeniedByMemberStatusAsync(member, AccessDenialReason.Suspended, pack);

        pack.VisitsRemaining.Should().Be(5);
    }

    [Fact]
    public async Task FrozenMember_WithActiveMembership_IsDeniedAsMemberInactive()
    {
        var member = MakeActiveMember();
        member.Freeze();

        await AssertDeniedByMemberStatusAsync(
            member, AccessDenialReason.MemberInactive, MakeActiveMembership(member.Id));
    }

    [Fact]
    public async Task CancelledMember_WithActiveMembership_IsDeniedAsMemberInactive()
    {
        var member = MakeActiveMember();
        member.Cancel();

        await AssertDeniedByMemberStatusAsync(
            member, AccessDenialReason.MemberInactive, MakeActiveMembership(member.Id));
    }

    [Fact]
    public async Task ProspectMember_IsDeniedAsMemberInactive()
    {
        var prospect = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Pía", "Gómez", DocumentType.DNI, "99999999", Gender.Female);

        await AssertDeniedByMemberStatusAsync(
            prospect, AccessDenialReason.MemberInactive, MakeActiveMembership(prospect.Id));
    }

    [Fact]
    public async Task SuspendedMember_WithoutActiveMembership_IsDeniedAsSuspended_NotAsIncompleteMembership()
    {
        var member = MakeActiveMember();
        member.Suspend();

        await AssertDeniedByMemberStatusAsync(member, AccessDenialReason.Suspended, membership: null);
    }

    [Fact]
    public async Task OverdueMember_BelowTheStopAmount_IsGranted()
    {
        var member = MakeActiveMember();
        member.MarkOverdue();
        member.Status.Should().Be(MemberStatus.Overdue);

        var result = await SwipeAsync(member, MakeActiveMembership(member.Id));

        result.Granted.Should().BeTrue("la deuda la resuelve StopOnOweAmount, no el estado");
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

    // ── Lector de códigos (AccessMethod.Barcode): tag del gimnasio, y si no hay, DNI ──────────

    private static ValidateSwipeRequest BarcodeSwipe(string code) =>
        new(code, AccessMethod.Barcode, 1, Guid.NewGuid(), Guid.NewGuid());

    private async Task AssertNoDocumentLookupAsync() =>
        await _memberRepo.DidNotReceive().FindByDocumentAsync(
            Arg.Any<DocumentType>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());

    [Fact]
    public async Task Barcode_ResolvesByTagSerial_AndLogsTheBarcodeMethod()
    {
        var member = MakeActiveMember();
        ArrangeGrantableSwipe(member, MakeActiveMembership(member.Id));   // TAG001 → member

        var result = await CreateSut().ValidateSwipeAsync(BarcodeSwipe("TAG001"));

        result.Granted.Should().BeTrue();
        result.MemberFullName.Should().Be("Juan Pérez");
        await AssertNoDocumentLookupAsync();
        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a => a.AccessGranted && a.Method == AccessMethod.Barcode && a.TagSerial == "TAG001"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Barcode_TagMatchWinsOverDni_EvenIfTheCodeLooksLikeOne()
    {
        var tagOwner = MakeActiveMember();
        _memberRepo.FindByTagSerialAsync("12345678", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(tagOwner);
        ArrangeGrantableSwipe(tagOwner, MakeActiveMembership(tagOwner.Id));

        var result = await CreateSut().ValidateSwipeAsync(BarcodeSwipe("12345678"));

        result.Granted.Should().BeTrue();
        result.MemberId.Should().Be(tagOwner.Id);
        await AssertNoDocumentLookupAsync();
    }

    [Fact]
    public async Task Barcode_WithoutTag_FallsBackToDniWhenTheCodeLooksLikeOne()
    {
        var member = MakeActiveMember();   // DNI 12345678
        ArrangeGrantableSwipe(member, MakeActiveMembership(member.Id));
        var request = BarcodeSwipe("12345678");
        _memberRepo.FindByTagSerialAsync("12345678", request.CompanyId, Arg.Any<CancellationToken>()).ReturnsNull();
        _memberRepo.FindByDocumentAsync(DocumentType.DNI, "12345678", request.CompanyId, Arg.Any<CancellationToken>())
            .Returns(member);

        var result = await CreateSut().ValidateSwipeAsync(request);

        result.Granted.Should().BeTrue();
        result.MemberFullName.Should().Be("Juan Pérez");
        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a => a.AccessGranted && a.Method == AccessMethod.Barcode),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Barcode_WithoutTag_NotDniShaped_DeniesAsTagUnknown_AndLogsIt()
    {
        _memberRepo.FindByTagSerialAsync("CARNET-X", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        var request = BarcodeSwipe("CARNET-X");

        var result = await CreateSut().ValidateSwipeAsync(request);

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
        await AssertNoDocumentLookupAsync();
        await _accessLogRepo.Received(1).AppendAsync(
            Arg.Is<AccessLog>(a =>
                !a.AccessGranted &&
                a.MemberId == null &&
                a.Method == AccessMethod.Barcode &&
                a.DenialReason == AccessDenialReason.TagUnknown &&
                a.TagSerial == "CARNET-X"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Barcode_DniShapedCodeThatIsNobodys_DeniesAsTagUnknown()
    {
        _memberRepo.FindByTagSerialAsync("99999999", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();
        _memberRepo.FindByDocumentAsync(DocumentType.DNI, "99999999", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(BarcodeSwipe("99999999"));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
    }

    [Fact]
    public async Task RfidCard_WithDniShapedCode_DoesNotFallBackToDni()
    {
        _memberRepo.FindByTagSerialAsync("12345678", Arg.Any<Guid>(), Arg.Any<CancellationToken>()).ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("12345678", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
        await AssertNoDocumentLookupAsync();
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
    public async Task OpenEndedMembership_NeverExpires()
    {
        var member = MakeActiveMember();
        ArrangeGrantableSwipe(member, MakeOpenEndedMembership(member.Id));

        var result = await CreateSut().ValidateSwipeAsync(TagSwipe());

        result.Granted.Should().BeTrue();
    }

    // ── Horario permitido: la zona es la de la empresa ───────────────────────

    // 14:00 UTC = 11:00 en Argentina (UTC-3) = 02:00 del día siguiente en Nueva Zelanda (UTC+12).
    private async Task<AccessDecision> SwipeWithMorningOnlyPlanAsync(string companyTimezone)
    {
        GivenCompanyIn(companyTimezone);
        var member = MakeActiveMember();
        var membership = MakeOpenEndedMembership(member.Id);
        typeof(MembershipType).GetProperty(nameof(MembershipType.ScheduleRestrictionJson))!.SetValue(
            membership.MembershipType, """[{"Days":[0,1,2,3,4,5,6],"From":"09:00:00","To":"12:00:00"}]""");
        ArrangeGrantableSwipe(member, membership, new DateTime(2026, 4, 21, 14, 0, 0, DateTimeKind.Utc));

        return await CreateSut().ValidateSwipeAsync(TagSwipe());
    }

    [Fact]
    public async Task Horario_UsesTheCompanyZone_AllowedInsideTheWindow()
    {
        var result = await SwipeWithMorningOnlyPlanAsync("Argentina Standard Time");

        result.Granted.Should().BeTrue();
    }

    [Fact]
    public async Task Horario_UsesTheCompanyZone_DeniedOutsideTheWindow()
    {
        var result = await SwipeWithMorningOnlyPlanAsync("New Zealand Standard Time");

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.OutOfHours);
    }
}
