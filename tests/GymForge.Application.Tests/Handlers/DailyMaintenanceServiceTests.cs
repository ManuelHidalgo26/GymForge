using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Maintenance;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Mantenimiento de UNA empresa en UN día local: reactiva congelamientos, vence, marca mora, dispara dunning y limpia la mora saldada.</summary>
public class DailyMaintenanceServiceTests
{
    private static readonly DateOnly Day = new(2026, 7, 21);

    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IDisposable _tenantScope = Substitute.For<IDisposable>();
    private readonly Company _company = Company.Create("Gym Test", "30-12345678-9");
    private readonly List<string> _calls = [];

    public DailyMaintenanceServiceTests()
    {
        _tenant.BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_tenantScope);
        _sender.SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true))
            .AndDoes(_ => _calls.Add("dunning"));
        _memberships.When(r => r.GetFreezeEndedAsync(_company.Id, Day, Arg.Any<CancellationToken>()))
            .Do(_ => _calls.Add("thaw"));
        _memberships.When(r => r.GetActiveExpiredAsync(_company.Id, Day, Arg.Any<CancellationToken>()))
            .Do(_ => _calls.Add("expire"));
        _charges.When(r => r.GetOverdueAsync(_company.Id, Day, Arg.Any<CancellationToken>()))
            .Do(_ => _calls.Add("overdue"));
        _members.When(r => r.GetByStatusAsync(_company.Id, MemberStatus.Overdue, Arg.Any<CancellationToken>()))
            .Do(_ => _calls.Add("clear"));
        HaveFreezeEnded();
        HaveExpired();
        HaveOverdue();
        HaveMembersInArrearsStatus();
    }

    private DailyMaintenanceService Sut(bool dunningEnabled = false) => new(
        _memberships, _charges, _members,
        new DunningService(
            _charges, _members, _sender, Substitute.For<INotificationLogRepository>(), _tenant,
            new DunningConfig { Enabled = dunningEnabled }),
        _tenant);

    private void HaveFreezeEnded(params Membership[] memberships) =>
        _memberships.GetFreezeEndedAsync(_company.Id, Day, Arg.Any<CancellationToken>())
            .Returns(memberships.ToList());

    private void HaveExpired(params Membership[] memberships) =>
        _memberships.GetActiveExpiredAsync(_company.Id, Day, Arg.Any<CancellationToken>())
            .Returns(memberships.ToList());

    private void HaveOverdue(params Charge[] charges) =>
        _charges.GetOverdueAsync(_company.Id, Day, Arg.Any<CancellationToken>())
            .Returns(charges.ToList());

    /// <summary>Socios que la base ya tiene en estado Overdue al llegar al último paso del día.</summary>
    private void HaveMembersInArrearsStatus(params Member[] members) =>
        _members.GetByStatusAsync(_company.Id, MemberStatus.Overdue, Arg.Any<CancellationToken>())
            .Returns(members.ToList());

    private Membership ActiveMembershipEndedYesterday()
    {
        var ms = Membership.Create(_company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Day.AddMonths(-1).AddDays(-1), Day.AddDays(-1));
        ms.Activate();
        return ms;
    }

    /// <summary>Membresía vigente seis meses más, congelada 10 días hasta <paramref name="freezeEnd"/>.</summary>
    private Membership FrozenUntil(DateOnly freezeEnd)
    {
        var ms = Membership.Create(_company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Day.AddMonths(-2), Day.AddMonths(6));
        ms.Activate();
        ms.Freeze(freezeEnd.AddDays(-10), freezeEnd, "Viaje");
        return ms;
    }

    private Charge ChargeDueDaysAgo(int daysOverdue, Member? member = null)
    {
        var charge = Charge.Create(_company.Id, Guid.NewGuid(), member?.Id ?? Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota", 10_000m, Day.AddDays(-daysOverdue));
        if (member is not null)
            typeof(Charge).GetProperty(nameof(Charge.Member))!.SetValue(charge, member);
        return charge;
    }

    private static Member ActiveMember()
    {
        var member = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        return member;
    }

    private static Member OverdueMember()
    {
        var member = ActiveMember();
        member.MarkOverdue();
        return member;
    }

    // ── a. Reactivar congelamientos terminados ────────────────────────────────

    [Fact]
    public async Task ThawsTheMembershipWhoseFreezeEndsOnTheDay()
    {
        var ms = FrozenUntil(Day);
        HaveFreezeEnded(ms);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        ms.Status.Should().Be(MembershipStatus.Active);
        ms.FreezeStart.Should().BeNull();
        ms.FreezeEnd.Should().BeNull();
        result.Unfrozen.Should().Be(1);
        _memberships.Received(1).Update(ms);
        await _memberships.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThawsAFreezeThatEndedBeforeTheDay_BecauseTheAppWasClosed()
    {
        var ms = FrozenUntil(Day.AddDays(-3));
        HaveFreezeEnded(ms);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        ms.Status.Should().Be(MembershipStatus.Active);
        result.Unfrozen.Should().Be(1);
    }

    [Fact]
    public async Task Thawing_KeepsTheExtendedEndDate_AndTheQuotaConsumed()
    {
        var ms = FrozenUntil(Day);
        var endDateWhileFrozen = ms.EndDate;
        HaveFreezeEnded(ms);

        await Sut().RunForCompanyDayAsync(_company, Day);

        ms.EndDate.Should().Be(endDateWhileFrozen, "los 10 días congelados ya se consumieron: no hay nada que devolver");
        ms.FreezeDaysUsedYear.Should().Be(10);
    }

    [Fact]
    public async Task AsksForTheFreezesEndedAsOfTheMaintenanceDay()
    {
        await Sut().RunForCompanyDayAsync(_company, Day);

        await _memberships.Received(1).GetFreezeEndedAsync(_company.Id, Day, Arg.Any<CancellationToken>());
    }

    // ── b. Vencer membresías ──────────────────────────────────────────────────

    [Fact]
    public async Task ExpiresTheActiveMembershipsThatEndedBeforeTheDay()
    {
        var ms = ActiveMembershipEndedYesterday();
        HaveExpired(ms);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        ms.Status.Should().Be(MembershipStatus.Expired);
        result.Expired.Should().Be(1);
        _memberships.Received(1).Update(ms);
        await _memberships.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── c. Marcar mora ────────────────────────────────────────────────────────

    [Fact]
    public async Task MarksTheOutstandingChargeAndItsMemberOverdue()
    {
        var member = ActiveMember();
        var charge = ChargeDueDaysAgo(5, member);
        HaveOverdue(charge);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        charge.Status.Should().Be(ChargeStatus.Overdue);
        member.Status.Should().Be(MemberStatus.Overdue);
        result.MarkedOverdue.Should().Be(1);
        _charges.Received(1).Update(charge);
        await _charges.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IgnoresChargesWithNothingOutstanding()
    {
        var member = ActiveMember();
        var settled = ChargeDueDaysAgo(5, member);
        settled.ApplyPayment(10_000m);
        HaveOverdue(settled);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        result.MarkedOverdue.Should().Be(0);
        member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task DoesNotPushARefundedChargeOrItsMemberIntoArrears()
    {
        var member = ActiveMember();
        var refunded = ChargeDueDaysAgo(5, member);
        typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(refunded, ChargeStatus.Refunded);
        HaveOverdue(refunded);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        refunded.Status.Should().Be(ChargeStatus.Refunded);
        member.Status.Should().Be(MemberStatus.Active);
        result.MarkedOverdue.Should().Be(0);
    }

    [Fact]
    public async Task ChargeAlreadyOverdue_IsNotCountedAgain_ButTheMemberStaysInArrears()
    {
        var member = ActiveMember();
        var charge = ChargeDueDaysAgo(5, member);
        charge.MarkOverdue();
        HaveOverdue(charge);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        result.MarkedOverdue.Should().Be(0);
        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task MemberThatCannotBeInArrears_KeepsItsStatus()
    {
        var member = ActiveMember();
        member.Suspend();
        var charge = ChargeDueDaysAgo(5, member);
        HaveOverdue(charge);

        await Sut().RunForCompanyDayAsync(_company, Day);

        member.Status.Should().Be(MemberStatus.Suspended);
    }

    // ── d. Dunning ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FiresTheDunningStageOfTheDay()
    {
        var member = ActiveMember();
        member.UpdateContact(null, "+5491155550000");
        _members.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        HaveOverdue(ChargeDueDaysAgo(7, member));   // etapa de 7 días

        var result = await Sut(dunningEnabled: true).RunForCompanyDayAsync(_company, Day);

        result.DunningSent.Should().Be(1);
        await _sender.Received(1).SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DunningSwitchedOff_SendsNothing()
    {
        var member = ActiveMember();
        member.UpdateContact(null, "+5491155550000");
        HaveOverdue(ChargeDueDaysAgo(7, member));

        var result = await Sut(dunningEnabled: false).RunForCompanyDayAsync(_company, Day);

        result.DunningSent.Should().Be(0);
        await _sender.DidNotReceive().SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
    }

    // ── e. Limpiar la mora saldada ────────────────────────────────────────────

    [Fact]
    public async Task ReturnsToActiveTheOverdueMemberThatNoLongerOwesAnOverdueCharge()
    {
        var member = OverdueMember();
        HaveMembersInArrearsStatus(member);

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        member.Status.Should().Be(MemberStatus.Active);
        result.ArrearsCleared.Should().Be(1);
        _members.Received(1).Update(member);
        await _members.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task KeepsInArrearsTheMemberThatStillOwesAnOverdueCharge()
    {
        var member = OverdueMember();
        HaveMembersInArrearsStatus(member);
        HaveOverdue(ChargeDueDaysAgo(5, member));

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        member.Status.Should().Be(MemberStatus.Overdue);
        result.ArrearsCleared.Should().Be(0);
        await _members.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PartiallyPaidOverdueCharge_StillKeepsTheMemberInArrears()
    {
        var member = OverdueMember();
        HaveMembersInArrearsStatus(member);
        var charge = ChargeDueDaysAgo(5, member);
        charge.ApplyPayment(4_000m);
        HaveOverdue(charge);

        await Sut().RunForCompanyDayAsync(_company, Day);

        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task ChargesThatNoLongerCount_DoNotKeepTheMemberInArrears()
    {
        var member = OverdueMember();
        HaveMembersInArrearsStatus(member);
        var settled = ChargeDueDaysAgo(5, member);
        settled.ApplyPayment(10_000m);
        var refunded = ChargeDueDaysAgo(8, member);
        typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(refunded, ChargeStatus.Refunded);
        HaveOverdue(settled, refunded);

        await Sut().RunForCompanyDayAsync(_company, Day);

        member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task OnlyTheMembersWithoutArrearsAreCleared()
    {
        var settled = OverdueMember();
        var stillOwing = OverdueMember();
        HaveMembersInArrearsStatus(settled, stillOwing);
        HaveOverdue(ChargeDueDaysAgo(5, stillOwing));

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        settled.Status.Should().Be(MemberStatus.Active);
        stillOwing.Status.Should().Be(MemberStatus.Overdue);
        result.ArrearsCleared.Should().Be(1);
    }

    [Fact]
    public async Task WithoutOverdueMembers_DoesNotEvenReadTheCharges()
    {
        await Sut().RunForCompanyDayAsync(_company, Day);

        // Solo la lectura de la mora del paso 2: el paso de limpieza corta apenas ve que no hay socios morosos.
        await _charges.Received(1).GetOverdueAsync(_company.Id, Day, Arg.Any<CancellationToken>());
    }

    // ── Orden, tenant y resultado ─────────────────────────────────────────────

    [Fact]
    public async Task RunsThawThenExpireThenOverdueThenDunningThenClearsTheArrears_InThatOrder()
    {
        var member = ActiveMember();
        member.UpdateContact(null, "+5491155550000");
        _members.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        HaveOverdue(ChargeDueDaysAgo(7, member));

        await Sut(dunningEnabled: true).RunForCompanyDayAsync(_company, Day);

        // "overdue" aparece dos veces: una al marcar la mora y otra dentro del dunning (que lee el mismo listado).
        _calls.Should().Equal("thaw", "expire", "overdue", "overdue", "dunning", "clear");
    }

    [Fact]
    public async Task WorksInsideTheCompanyScope_AndReleasesIt()
    {
        await Sut().RunForCompanyDayAsync(_company, Day);

        _tenant.Received().BeginScope(_company.Id, Guid.Empty);
        _tenantScope.Received(1).Dispose();
    }

    [Fact]
    public async Task ReturnsTheCountsOfTheDay()
    {
        var member = ActiveMember();
        HaveFreezeEnded(FrozenUntil(Day));
        HaveExpired(ActiveMembershipEndedYesterday(), ActiveMembershipEndedYesterday());
        HaveOverdue(ChargeDueDaysAgo(2, member), ChargeDueDaysAgo(9, member));
        HaveMembersInArrearsStatus(OverdueMember());

        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        result.Should().Be(new MaintenanceDayResult(Unfrozen: 1, Expired: 2, MarkedOverdue: 2, DunningSent: 0, ArrearsCleared: 1));
    }

    [Fact]
    public async Task AnEmptyDay_ChangesAndSavesNothing()
    {
        var result = await Sut().RunForCompanyDayAsync(_company, Day);

        result.Should().Be(default(MaintenanceDayResult));
        await _memberships.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _charges.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _members.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
