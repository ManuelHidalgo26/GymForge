using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
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
    private readonly IClock _clock = Substitute.For<IClock>();

    private readonly GatekeeperConfig _config = new()
    {
        StopOnOweAmount = 10_000m,
        WarnOnOweAmount = 5_000m,
        Timezone = "UTC",
        AntiPassbackMinutes = 5
    };

    private ValidateSwipeUseCase CreateSut() =>
        new(_memberRepo, _membershipRepo, _chargeRepo, _accessLogRepo, _eventBus, _clock, _config);

    private static Member MakeActiveMember()
    {
        var m = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Juan", "Pérez",
            DocumentType.DNI, "12345678", Gender.Male);
        m.Activate(DateOnly.FromDateTime(DateTime.Today));
        return m;
    }

    private static Membership MakeActiveMembership(Guid memberId, DateOnly? start = null, DateOnly? end = null)
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), memberId, Guid.NewGuid(),
            start ?? DateOnly.FromDateTime(DateTime.Today),
            end ?? DateOnly.FromDateTime(DateTime.Today.AddMonths(1)));
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
        _clock.Now.Returns(now);
        _clock.Today.Returns(DateOnly.FromDateTime(now));

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByTagSerialAsync("TAG001", Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(0m);
        _accessLogRepo.GetLastAsync(member.Id, 1, Arg.Any<CancellationToken>())
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

    // ── Denial scenarios ──────────────────────────────────────────────────────

    [Fact]
    public async Task UnknownTag_DeniesWithTagUnknown()
    {
        _clock.Now.Returns(DateTime.UtcNow);
        _clock.Today.Returns(DateOnly.FromDateTime(DateTime.Today));
        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var result = await CreateSut().ValidateSwipeAsync(
            new ValidateSwipeRequest("UNKNOWN", AccessMethod.RfidCard, 1, Guid.NewGuid(), Guid.NewGuid()));

        result.Granted.Should().BeFalse();
        result.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
    }

    [Fact]
    public async Task NoActiveMembership_DeniesWithIncompleteMembership()
    {
        _clock.Now.Returns(DateTime.UtcNow);
        _clock.Today.Returns(DateOnly.FromDateTime(DateTime.Today));
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
        _clock.Now.Returns(now);
        _clock.Today.Returns(DateOnly.FromDateTime(now));

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
        _clock.Now.Returns(now);
        _clock.Today.Returns(DateOnly.FromDateTime(now));

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
        _accessLogRepo.GetLastAsync(member.Id, 1, Arg.Any<CancellationToken>())
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
        _clock.Now.Returns(now);
        _clock.Today.Returns(DateOnly.FromDateTime(now));

        var member = MakeActiveMember();
        var membership = MakeActiveMembership(member.Id);

        _memberRepo.FindByTagSerialAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(member);
        _membershipRepo.GetCurrentActiveAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(membership);
        _chargeRepo.SumOutstandingAsync(member.Id, Arg.Any<CancellationToken>())
            .Returns(7_000m);  // > WarnOnOweAmount (5_000) but < StopOnOweAmount (10_000)
        _accessLogRepo.GetLastAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
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
        _clock.Now.Returns(now);
        _clock.Today.Returns(DateOnly.FromDateTime(now));

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
}
