using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class MemberCreditMovementTests
{
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _siteId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();

    // ── Excedente de pago ─────────────────────────────────────────────────────

    [Fact]
    public void CreditFromOverpayment_AddsPositiveCreditLinkedToThePayment()
    {
        var paymentId = Guid.NewGuid();

        var movement = MemberCreditMovement.CreditFromOverpayment(_companyId, _siteId, _memberId, 15_000m, paymentId);

        movement.Amount.Should().Be(15_000m);
        movement.Type.Should().Be(CreditMovementType.Overpayment);
        movement.PaymentId.Should().Be(paymentId);
        movement.ChargeId.Should().BeNull();
        movement.CompanyId.Should().Be(_companyId);
        movement.SiteId.Should().Be(_siteId);
        movement.MemberId.Should().Be(_memberId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CreditFromOverpayment_NonPositiveAmount_Throws(double amount)
    {
        var act = () => MemberCreditMovement.CreditFromOverpayment(
            _companyId, _siteId, _memberId, (decimal)amount, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    // ── Consumo contra un cargo ───────────────────────────────────────────────

    [Fact]
    public void DebitForCharge_StoresTheAmountAsNegative()
    {
        var chargeId = Guid.NewGuid();

        var movement = MemberCreditMovement.DebitForCharge(_companyId, _siteId, _memberId, 8_000m, chargeId);

        movement.Amount.Should().Be(-8_000m);
        movement.Type.Should().Be(CreditMovementType.AppliedToCharge);
        movement.ChargeId.Should().Be(chargeId);
        movement.PaymentId.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DebitForCharge_NonPositiveAmount_Throws(double amount)
    {
        var act = () => MemberCreditMovement.DebitForCharge(
            _companyId, _siteId, _memberId, (decimal)amount, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    // ── Ajuste manual ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2_500)]
    [InlineData(-2_500)]
    public void Adjustment_KeepsTheSignAndTheReason(double signedAmount)
    {
        var movement = MemberCreditMovement.Adjustment(
            _companyId, _siteId, _memberId, (decimal)signedAmount, "Corrección de caja");

        movement.Amount.Should().Be((decimal)signedAmount);
        movement.Type.Should().Be(CreditMovementType.Adjustment);
        movement.Note.Should().Be("Corrección de caja");
    }

    [Fact]
    public void Adjustment_Zero_Throws()
    {
        var act = () => MemberCreditMovement.Adjustment(_companyId, _siteId, _memberId, 0m, "Nada");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjustment_WithoutReason_Throws(string note)
    {
        var act = () => MemberCreditMovement.Adjustment(_companyId, _siteId, _memberId, 100m, note);

        act.Should().Throw<ArgumentException>();
    }
}
