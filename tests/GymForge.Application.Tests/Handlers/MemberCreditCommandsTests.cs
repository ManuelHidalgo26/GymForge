using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class MemberCreditCommandsTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _siteId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();

    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();
    private readonly IMemberCreditRepository _creditRepo = Substitute.For<IMemberCreditRepository>();
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly List<MemberCreditMovement> _recorded = [];

    public MemberCreditCommandsTests()
    {
        _creditRepo.AddAsync(Arg.Do<MemberCreditMovement>(_recorded.Add), Arg.Any<CancellationToken>());
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);
    }

    private ApplyMemberCreditCommandHandler ApplySut() =>
        new(_chargeRepo, _creditRepo, new ArrearsRelief(_memberRepo, _chargeRepo, _calendar));

    private ApplyMemberCreditCommand Cmd(params Guid[] chargeIds) =>
        new(_companyId, _siteId, _memberId, chargeIds.Length == 0 ? null : chargeIds);

    private Charge ChargeOf(decimal amount, DateOnly dueDate, Guid? memberId = null, Guid? companyId = null) =>
        Charge.Create(companyId ?? _companyId, _siteId, memberId ?? _memberId, null,
            ConceptType.MembershipFee, "Cuota", amount, dueDate);

    private void GivenBalance(decimal balance) =>
        _creditRepo.GetBalanceAsync(_memberId, Arg.Any<CancellationToken>()).Returns(balance);

    private void GivenPending(params Charge[] charges) =>
        _chargeRepo.GetPendingAsync(_memberId, Arg.Any<CancellationToken>()).Returns(charges.ToList());

    /// <summary>Charge no expone un método para pasar a Refunded: se fuerza el estado por reflexión.</summary>
    private static void ForceStatus(Charge charge, ChargeStatus status) =>
        typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(charge, status);

    // ── Aplicar crédito ───────────────────────────────────────────────────────

    [Fact]
    public async Task Apply_SettlesTheOldestChargesFirst()
    {
        var older = ChargeOf(10_000m, new DateOnly(2026, 3, 1));
        var newer = ChargeOf(10_000m, new DateOnly(2026, 4, 1));
        GivenBalance(12_000m);
        GivenPending(newer, older);   // fuera de orden a propósito

        var result = await ApplySut().Handle(Cmd(), CancellationToken.None);

        older.Status.Should().Be(ChargeStatus.Paid);
        newer.Status.Should().Be(ChargeStatus.PartiallyPaid);
        newer.AmountOutstanding.Should().Be(8_000m);
        result.Applied.Should().Be(12_000m);
        result.RemainingCredit.Should().Be(0m);
    }

    [Fact]
    public async Task Apply_RecordsOneDebitPerSettledCharge_InTheSameSave()
    {
        var older = ChargeOf(10_000m, new DateOnly(2026, 3, 1));
        var newer = ChargeOf(10_000m, new DateOnly(2026, 4, 1));
        GivenBalance(12_000m);
        GivenPending(older, newer);

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        _recorded.Should().HaveCount(2);
        _recorded.Should().OnlyContain(m => m.Type == CreditMovementType.AppliedToCharge && m.Amount < 0);
        _recorded.Sum(m => m.Amount).Should().Be(-12_000m);
        _recorded.Select(m => m.ChargeId).Should().BeEquivalentTo(new Guid?[] { older.Id, newer.Id });
        _recorded.Should().OnlyContain(m =>
            m.CompanyId == _companyId && m.SiteId == _siteId && m.MemberId == _memberId);
        await _chargeRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_CreditBiggerThanDebt_LeavesTheRest()
    {
        var charge = ChargeOf(5_000m, new DateOnly(2026, 3, 1));
        GivenBalance(20_000m);
        GivenPending(charge);

        var result = await ApplySut().Handle(Cmd(), CancellationToken.None);

        charge.Status.Should().Be(ChargeStatus.Paid);
        result.Applied.Should().Be(5_000m);
        result.RemainingCredit.Should().Be(15_000m);
    }

    [Fact]
    public async Task Apply_NoBalance_IsANoOp()
    {
        var charge = ChargeOf(5_000m, new DateOnly(2026, 3, 1));
        GivenBalance(0m);
        GivenPending(charge);

        var result = await ApplySut().Handle(Cmd(), CancellationToken.None);

        result.Should().Be(new ApplyCreditResult(0m, 0m));
        charge.AmountPaid.Should().Be(0m);
        await _creditRepo.DidNotReceive().AddAsync(Arg.Any<MemberCreditMovement>(), Arg.Any<CancellationToken>());
        await _chargeRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_NoPendingCharges_KeepsTheCredit()
    {
        GivenBalance(7_000m);
        GivenPending();

        var result = await ApplySut().Handle(Cmd(), CancellationToken.None);

        result.Applied.Should().Be(0m);
        result.RemainingCredit.Should().Be(7_000m);
        await _creditRepo.DidNotReceive().AddAsync(Arg.Any<MemberCreditMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_ExplicitCharges_OnlySettlesThose()
    {
        var chosen = ChargeOf(5_000m, new DateOnly(2026, 4, 1));
        var other = ChargeOf(5_000m, new DateOnly(2026, 3, 1));
        GivenBalance(20_000m);
        GivenPending(other, chosen);
        _chargeRepo.GetByIdAsync(chosen.Id, Arg.Any<CancellationToken>()).Returns(chosen);

        await ApplySut().Handle(Cmd(chosen.Id), CancellationToken.None);

        chosen.Status.Should().Be(ChargeStatus.Paid);
        other.AmountPaid.Should().Be(0m);
    }

    [Fact]
    public async Task Apply_ChargeOfAnotherMember_Throws()
    {
        var foreign = ChargeOf(5_000m, new DateOnly(2026, 3, 1), memberId: Guid.NewGuid());
        GivenBalance(20_000m);
        _chargeRepo.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var act = () => ApplySut().Handle(Cmd(foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        foreign.AmountPaid.Should().Be(0m);
        await _creditRepo.DidNotReceive().AddAsync(Arg.Any<MemberCreditMovement>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_ChargeOfAnotherCompany_Throws()
    {
        var foreign = ChargeOf(5_000m, new DateOnly(2026, 3, 1), companyId: Guid.NewGuid());
        GivenBalance(20_000m);
        _chargeRepo.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var act = () => ApplySut().Handle(Cmd(foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Apply_UnknownChargeId_Throws()
    {
        GivenBalance(20_000m);
        _chargeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Charge?)null);

        var act = () => ApplySut().Handle(Cmd(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Theory]
    [InlineData(ChargeStatus.WrittenOff)]
    [InlineData(ChargeStatus.Refunded)]
    public async Task Apply_ExplicitChargeThatIsNoLongerCollectable_IsSkipped(ChargeStatus status)
    {
        var closed = ChargeOf(5_000m, new DateOnly(2026, 3, 1));
        ForceStatus(closed, status);
        GivenBalance(20_000m);
        _chargeRepo.GetByIdAsync(closed.Id, Arg.Any<CancellationToken>()).Returns(closed);

        var result = await ApplySut().Handle(Cmd(closed.Id), CancellationToken.None);

        closed.AmountPaid.Should().Be(0m);
        closed.Status.Should().Be(status);
        result.Applied.Should().Be(0m);
    }

    // ── Salida de mora ────────────────────────────────────────────────────────

    private Member GivenOverdueMember()
    {
        var member = Member.Create(_companyId, _siteId, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        member.MarkOverdue();
        _memberRepo.GetByIdAsync(_memberId, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    [Fact]
    public async Task Apply_SettlingTheLastOverdueCharge_ReturnsTheMemberToActive()
    {
        var member = GivenOverdueMember();
        GivenBalance(10_000m);
        GivenPending(ChargeOf(10_000m, Today.AddDays(-10)));

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task Apply_AnotherOverdueChargeRemains_TheMemberStaysOverdue()
    {
        var member = GivenOverdueMember();
        GivenBalance(10_000m);
        GivenPending(ChargeOf(10_000m, Today.AddDays(-40)), ChargeOf(10_000m, Today.AddDays(-10)));

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Apply_PartiallyCoveringTheOverdueCharge_TheMemberStaysOverdue()
    {
        var member = GivenOverdueMember();
        GivenBalance(3_000m);
        GivenPending(ChargeOf(10_000m, Today.AddDays(-10)));

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Apply_ClearsTheArrearsInTheSameSaveAsTheLedger()
    {
        var member = GivenOverdueMember();
        GivenBalance(10_000m);
        GivenPending(ChargeOf(10_000m, Today.AddDays(-10)));
        MemberStatus? statusWhenSaving = null;
        _chargeRepo.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => statusWhenSaving = member.Status);

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        statusWhenSaving.Should().Be(MemberStatus.Active);
        await _chargeRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Apply_WithoutBalance_LeavesTheMemberInArrears()
    {
        var member = GivenOverdueMember();
        GivenBalance(0m);
        GivenPending(ChargeOf(10_000m, Today.AddDays(-10)));

        await ApplySut().Handle(Cmd(), CancellationToken.None);

        member.Status.Should().Be(MemberStatus.Overdue);
        await _memberRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_EmptyIds_Fails() =>
        new ApplyMemberCreditCommandValidator()
            .Validate(new ApplyMemberCreditCommand(Guid.Empty, Guid.Empty, Guid.Empty))
            .IsValid.Should().BeFalse();

    // ── Consulta de saldo ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetBalance_ReturnsTheLedgerBalance()
    {
        GivenBalance(15_000m);

        var balance = await new GetMemberCreditBalanceQueryHandler(_creditRepo)
            .Handle(new GetMemberCreditBalanceQuery(_memberId), CancellationToken.None);

        balance.Should().Be(15_000m);
    }
}
