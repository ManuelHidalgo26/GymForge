using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ProcessPaymentCommandTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _siteId = Guid.NewGuid();

    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();
    private readonly IPaymentRepository _paymentRepo = Substitute.For<IPaymentRepository>();
    private readonly ICashRegister _cashRegister = Substitute.For<ICashRegister>();
    private readonly IMemberCreditRepository _creditRepo = Substitute.For<IMemberCreditRepository>();
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();

    public ProcessPaymentCommandTests() =>
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);

    private ProcessPaymentCommandHandler Sut() => new(
        _chargeRepo, _paymentRepo, _cashRegister, _creditRepo,
        new ArrearsRelief(_memberRepo, _chargeRepo, _calendar));

    private static ProcessPaymentCommand Cmd(decimal amount, PaymentMethod method, string? last4 = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Amount: amount, Method: method, ShiftId: null, CardLast4: last4);

    [Fact]
    public async Task Handle_AutoAllocatesOldestChargeFirst_AndPersistsPayment()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();

        var older = Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota marzo", 10_000m, new DateOnly(2026, 3, 1));
        var newer = Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota abril", 10_000m, new DateOnly(2026, 4, 1));

        // Devuelto fuera de orden a propósito: el handler debe ordenar por DueDate.
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>())
            .Returns(new List<Charge> { newer, older });

        var cmd = new ProcessPaymentCommand(
            companyId, siteId, memberId, Guid.NewGuid(),
            Amount: 12_000m, Method: PaymentMethod.Cash, ShiftId: shiftId);

        var dto = await Sut().Handle(cmd, CancellationToken.None);

        older.AmountOutstanding.Should().Be(0m);
        older.Status.Should().Be(ChargeStatus.Paid);
        newer.AmountOutstanding.Should().Be(8_000m);
        newer.Status.Should().Be(ChargeStatus.PartiallyPaid);

        dto.Allocations.Should().HaveCount(2);
        dto.Allocations.Sum(a => a.Amount).Should().Be(12_000m);

        // El pago se persiste y la caja recibe el impacto del efectivo.
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _cashRegister.Received(1).PostIfCashAsync(
            PaymentMethod.Cash, shiftId, CashMovementCategory.Membership, 12_000m,
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await _chargeRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Cargos explícitos: existencia y pertenencia ──────────────────────────

    private static ProcessPaymentCommand CmdForCharges(
        Guid companyId, Guid siteId, Guid memberId, params Guid[] chargeIds) =>
        new(companyId, siteId, memberId, Guid.NewGuid(),
            Amount: 10_000m, Method: PaymentMethod.Cash, ShiftId: null, ChargeIds: chargeIds);

    private static Charge ChargeOf(Guid companyId, Guid siteId, Guid memberId) =>
        Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, new DateOnly(2026, 4, 1));

    // ── Cargos explícitos: solo los que admiten cobro ────────────────────────

    private async Task AssertExplicitChargeIsRejectedAsync(Action<Charge> bringToStatus, ChargeStatus expected)
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var charge = ChargeOf(companyId, siteId, memberId);
        bringToStatus(charge);
        charge.Status.Should().Be(expected);
        _chargeRepo.GetByIdAsync(charge.Id, Arg.Any<CancellationToken>()).Returns(charge);

        var act = () => Sut().Handle(
            CmdForCharges(companyId, siteId, memberId, charge.Id), CancellationToken.None);

        await act.Should().ThrowAsync<ChargeNotCollectableException>().WithMessage("*no admite cobro*");
        charge.Status.Should().Be(expected);
        charge.AmountPaid.Should().Be(0m);
        await _paymentRepo.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _chargeRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public Task Handle_ExplicitWrittenOffCharge_ThrowsAndDoesNotAllocate() =>
        AssertExplicitChargeIsRejectedAsync(c => c.WriteOff("incobrable"), ChargeStatus.WrittenOff);

    // Reflexión a propósito: el dominio todavía no tiene una transición hacia Refunded (Status tiene private set).
    // Cuando exista el flujo real de reembolso, ese método reemplaza esta reflexión.
    [Fact]
    public Task Handle_ExplicitRefundedCharge_ThrowsAndDoesNotAllocate() =>
        AssertExplicitChargeIsRejectedAsync(
            c => typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(c, ChargeStatus.Refunded),
            ChargeStatus.Refunded);

    [Fact]
    public async Task Handle_ExplicitChargesOfTheMember_AllocatesThem()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var charge = ChargeOf(companyId, siteId, memberId);
        _chargeRepo.GetByIdAsync(charge.Id, Arg.Any<CancellationToken>()).Returns(charge);

        var dto = await Sut().Handle(CmdForCharges(companyId, siteId, memberId, charge.Id), CancellationToken.None);

        dto.Allocations.Should().ContainSingle();
        charge.Status.Should().Be(ChargeStatus.Paid);
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChargeOfAnotherMember_Throws()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var foreignCharge = ChargeOf(companyId, siteId, memberId: Guid.NewGuid());
        _chargeRepo.GetByIdAsync(foreignCharge.Id, Arg.Any<CancellationToken>()).Returns(foreignCharge);

        var act = () => Sut().Handle(
            CmdForCharges(companyId, siteId, Guid.NewGuid(), foreignCharge.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        foreignCharge.AmountPaid.Should().Be(0m);
        await _paymentRepo.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChargeOfAnotherCompany_Throws()
    {
        var memberId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var foreignCharge = ChargeOf(companyId: Guid.NewGuid(), siteId, memberId);
        _chargeRepo.GetByIdAsync(foreignCharge.Id, Arg.Any<CancellationToken>()).Returns(foreignCharge);

        var act = () => Sut().Handle(
            CmdForCharges(Guid.NewGuid(), siteId, memberId, foreignCharge.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _paymentRepo.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownChargeId_Throws()
    {
        _chargeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Charge?)null);

        var act = () => Sut().Handle(
            CmdForCharges(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _paymentRepo.DidNotReceive().AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
    }

    // ── Excedente → saldo a favor ────────────────────────────────────────────

    [Fact]
    public async Task Handle_Overpayment_CreatesCredit()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var charge = Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota", 35_000m, new DateOnly(2026, 4, 1));
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>()).Returns(new List<Charge> { charge });

        var dto = await Sut().Handle(
            new ProcessPaymentCommand(companyId, siteId, memberId, Guid.NewGuid(),
                Amount: 50_000m, Method: PaymentMethod.Cash, ShiftId: null),
            CancellationToken.None);

        charge.Status.Should().Be(ChargeStatus.Paid);
        await _creditRepo.Received(1).AddAsync(
            Arg.Is<MemberCreditMovement>(m =>
                m.Amount == 15_000m &&
                m.Type == CreditMovementType.Overpayment &&
                m.PaymentId == dto.Id &&
                m.MemberId == memberId && m.CompanyId == companyId && m.SiteId == siteId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Overpayment_CreditIsQueuedBeforeTheSingleSave()
    {
        var memberId = Guid.NewGuid();
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>()).Returns(new List<Charge>());

        await Sut().Handle(
            new ProcessPaymentCommand(Guid.NewGuid(), Guid.NewGuid(), memberId, Guid.NewGuid(),
                Amount: 5_000m, Method: PaymentMethod.Cash, ShiftId: null),
            CancellationToken.None);

        // Pago, crédito y cargos viajan en el mismo SaveChanges (misma transacción).
        Received.InOrder(() =>
        {
            _paymentRepo.AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
            _creditRepo.AddAsync(Arg.Any<MemberCreditMovement>(), Arg.Any<CancellationToken>());
            _chargeRepo.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
        await _creditRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoPendingCharges_WholeAmountBecomesCredit()
    {
        var memberId = Guid.NewGuid();
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>()).Returns(new List<Charge>());

        await Sut().Handle(
            new ProcessPaymentCommand(Guid.NewGuid(), Guid.NewGuid(), memberId, Guid.NewGuid(),
                Amount: 5_000m, Method: PaymentMethod.Cash, ShiftId: null),
            CancellationToken.None);

        await _creditRepo.Received(1).AddAsync(
            Arg.Is<MemberCreditMovement>(m => m.Amount == 5_000m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExactAmount_NoCredit()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();
        var charge = ChargeOf(companyId, siteId, memberId);
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>()).Returns(new List<Charge> { charge });

        await Sut().Handle(
            new ProcessPaymentCommand(companyId, siteId, memberId, Guid.NewGuid(),
                Amount: 10_000m, Method: PaymentMethod.Cash, ShiftId: null),
            CancellationToken.None);

        await _creditRepo.DidNotReceive().AddAsync(Arg.Any<MemberCreditMovement>(), Arg.Any<CancellationToken>());
    }

    // ── Salida de mora ───────────────────────────────────────────────────────

    private Member OverdueMember()
    {
        var member = Member.Create(_companyId, _siteId, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        member.MarkOverdue();
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    private Charge OverdueCharge(Member member, int daysOverdue) =>
        Charge.Create(_companyId, _siteId, member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, Today.AddDays(-daysOverdue));

    private void GivenPending(Member member, params Charge[] charges) =>
        _chargeRepo.GetPendingAsync(member.Id, Arg.Any<CancellationToken>()).Returns(charges.ToList());

    private Task<PaymentDto> PayAsync(Member member, decimal amount, params Guid[] chargeIds) =>
        Sut().Handle(
            new ProcessPaymentCommand(_companyId, _siteId, member.Id, Guid.NewGuid(),
                Amount: amount, Method: PaymentMethod.Cash, ShiftId: null,
                ChargeIds: chargeIds.Length == 0 ? null : chargeIds),
            CancellationToken.None);

    [Fact]
    public async Task Handle_SettlingTheLastOverdueCharge_ReturnsTheMemberToActive()
    {
        var member = OverdueMember();
        GivenPending(member, OverdueCharge(member, 10));

        await PayAsync(member, 10_000m);

        member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task Handle_AnotherOverdueChargeRemains_TheMemberStaysOverdue()
    {
        var member = OverdueMember();
        GivenPending(member, OverdueCharge(member, 40), OverdueCharge(member, 10));

        await PayAsync(member, 10_000m);   // salda la más vieja; queda la otra

        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Handle_PayingOneExplicitChargeWhileAnotherIsOverdue_TheMemberStaysOverdue()
    {
        var member = OverdueMember();
        var chosen = OverdueCharge(member, 10);
        var other = OverdueCharge(member, 40);
        _chargeRepo.GetByIdAsync(chosen.Id, Arg.Any<CancellationToken>()).Returns(chosen);
        GivenPending(member, chosen, other);

        await PayAsync(member, 10_000m, chosen.Id);

        chosen.Status.Should().Be(ChargeStatus.Paid);
        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Handle_PartialPaymentOfTheOnlyOverdueCharge_TheMemberStaysOverdue()
    {
        var member = OverdueMember();
        GivenPending(member, OverdueCharge(member, 10));

        await PayAsync(member, 4_000m);

        member.Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Handle_ClearsTheArrearsInTheSameSaveAsThePayment()
    {
        var member = OverdueMember();
        GivenPending(member, OverdueCharge(member, 10));
        MemberStatus? statusWhenSaving = null;
        _chargeRepo.When(r => r.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => statusWhenSaving = member.Status);

        await PayAsync(member, 10_000m);

        statusWhenSaving.Should().Be(MemberStatus.Active, "el estado viaja en el mismo SaveChanges que el cobro");
        await _chargeRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_NonPositiveAmount_Fails() =>
        new ProcessPaymentCommandValidator().Validate(Cmd(0m, PaymentMethod.Cash)).IsValid.Should().BeFalse();

    [Fact]
    public void Validator_CardWithoutLast4_Fails() =>
        new ProcessPaymentCommandValidator().Validate(Cmd(5_000m, PaymentMethod.CreditCard)).IsValid.Should().BeFalse();

    [Fact]
    public void Validator_ValidCashPayment_Passes() =>
        new ProcessPaymentCommandValidator().Validate(Cmd(5_000m, PaymentMethod.Cash)).IsValid.Should().BeTrue();
}
