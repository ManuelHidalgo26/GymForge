using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Services;

/// <summary>Devuelve a Active al socio moroso cuando salda su última deuda vencida.</summary>
public class ArrearsReliefTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly Member _member;

    public ArrearsReliefTests()
    {
        _member = NewMember();
        _members.GetByIdAsync(_member.Id, Arg.Any<CancellationToken>()).Returns(_member);
        _calendar.TodayAsync(_companyId, Arg.Any<CancellationToken>()).Returns(Today);
        GivenPending();
    }

    private ArrearsRelief Sut() => new(_members, _charges, _calendar);

    private Member NewMember()
    {
        var member = Member.Create(_companyId, Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        member.MarkOverdue();
        return member;
    }

    private Charge ChargeDue(DateOnly dueDate, decimal paid = 0m)
    {
        var charge = Charge.Create(_companyId, _member.SiteId, _member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, dueDate);
        if (paid > 0) charge.ApplyPayment(paid);
        return charge;
    }

    private void GivenPending(params Charge[] charges) =>
        _charges.GetPendingAsync(_member.Id, Arg.Any<CancellationToken>()).Returns(charges.ToList());

    // ── Sale de mora ──────────────────────────────────────────────────────────

    [Fact]
    public async Task NothingOverdueLeft_ReturnsTheMemberToActive()
    {
        GivenPending();

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task OnlyChargesNotDueYet_ReturnsTheMemberToActive()
    {
        GivenPending(ChargeDue(Today), ChargeDue(Today.AddDays(10)));

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task ChargeSettledInMemoryButStillListedAsPending_DoesNotKeepTheMemberInArrears()
    {
        // El repositorio devuelve lo que la base todavía ve como pendiente; el cobro recién aplicado vive en memoria.
        var justPaid = ChargeDue(Today.AddDays(-5), paid: 10_000m);
        GivenPending(justPaid);

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Active);
    }

    // ── Sigue en mora ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AnotherOverdueChargeRemains_KeepsTheMemberOverdue()
    {
        GivenPending(ChargeDue(Today.AddDays(-30)));

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task PartiallyPaidOverdueCharge_KeepsTheMemberOverdue()
    {
        GivenPending(ChargeDue(Today.AddDays(-30), paid: 4_000m));

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Overdue);
    }

    // ── Lo que no se toca ─────────────────────────────────────────────────────

    [Fact]
    public async Task MemberNotInArrears_IsLeftAloneWithoutQueryingCharges()
    {
        _member.ClearArrears();

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Active);
        await _charges.DidNotReceive().GetPendingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _calendar.DidNotReceive().TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuspendedMember_IsNotReactivatedByPayingTheDebt()
    {
        _member.Suspend();

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        _member.Status.Should().Be(MemberStatus.Suspended);
    }

    [Fact]
    public async Task UnknownMember_IsANoOp()
    {
        var act = () => Sut().ClearIfSettledAsync(_companyId, Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ComparesAgainstTheLocalDayOfTheCompany()
    {
        // Vence el 21/07 y hoy (local) es el 21/07: todavía no está vencido, aunque en UTC ya sea el 22.
        GivenPending(ChargeDue(Today));

        await Sut().ClearIfSettledAsync(_companyId, _member.Id);

        await _calendar.Received(1).TodayAsync(_companyId, Arg.Any<CancellationToken>());
        _member.Status.Should().Be(MemberStatus.Active);
    }
}
