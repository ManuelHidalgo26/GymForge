using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ProcessPaymentCommandTests
{
    private readonly IChargeRepository _chargeRepo = Substitute.For<IChargeRepository>();

    private static ProcessPaymentCommand Cmd(decimal amount, PaymentMethod method, string? last4 = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Amount: amount, Method: method, ShiftId: null, CardLast4: last4);

    [Fact]
    public async Task Handle_AutoAllocatesOldestChargeFirst()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var memberId = Guid.NewGuid();

        var older = Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota marzo", 10_000m, new DateOnly(2026, 3, 1));
        var newer = Charge.Create(companyId, siteId, memberId, null,
            ConceptType.MembershipFee, "Cuota abril", 10_000m, new DateOnly(2026, 4, 1));

        // Devuelto fuera de orden a propósito: el handler debe ordenar por DueDate.
        _chargeRepo.GetPendingAsync(memberId, Arg.Any<CancellationToken>())
            .Returns(new List<Charge> { newer, older });

        var cmd = new ProcessPaymentCommand(
            companyId, siteId, memberId, Guid.NewGuid(),
            Amount: 12_000m, Method: PaymentMethod.Cash, ShiftId: null);

        var dto = await new ProcessPaymentCommandHandler(_chargeRepo).Handle(cmd, CancellationToken.None);

        older.AmountOutstanding.Should().Be(0m);
        older.Status.Should().Be(ChargeStatus.Paid);
        newer.AmountOutstanding.Should().Be(8_000m);
        newer.Status.Should().Be(ChargeStatus.PartiallyPaid);

        dto.Allocations.Should().HaveCount(2);
        dto.Allocations.Sum(a => a.Amount).Should().Be(12_000m);
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
