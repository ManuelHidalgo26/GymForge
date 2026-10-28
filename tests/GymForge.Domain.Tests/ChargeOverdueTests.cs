using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class ChargeOverdueTests
{
    private static Charge NewCharge() =>
        Charge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota", 10_000m, new DateOnly(2026, 4, 1));

    /// <summary>El dominio todavía no expone un método para reembolsar un cargo: se fuerza el estado.</summary>
    private static Charge RefundedCharge()
    {
        var charge = NewCharge();
        typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(charge, ChargeStatus.Refunded);
        return charge;
    }

    [Theory]
    [InlineData(ChargeStatus.Pending)]
    [InlineData(ChargeStatus.PartiallyPaid)]
    public void MarkOverdue_OpenCharge_BecomesOverdue(ChargeStatus status)
    {
        var charge = NewCharge();
        if (status == ChargeStatus.PartiallyPaid) charge.ApplyPayment(1_000m);

        charge.MarkOverdue();

        charge.Status.Should().Be(ChargeStatus.Overdue);
    }

    [Fact]
    public void MarkOverdue_PaidCharge_StaysPaid()
    {
        var charge = NewCharge();
        charge.ApplyPayment(10_000m);

        charge.MarkOverdue();

        charge.Status.Should().Be(ChargeStatus.Paid);
    }

    [Fact]
    public void MarkOverdue_WrittenOffCharge_StaysWrittenOff()
    {
        var charge = NewCharge();
        charge.WriteOff("Incobrable");

        charge.MarkOverdue();

        charge.Status.Should().Be(ChargeStatus.WrittenOff);
    }

    [Fact]
    public void MarkOverdue_RefundedCharge_StaysRefunded()
    {
        var charge = RefundedCharge();

        charge.MarkOverdue();

        charge.Status.Should().Be(ChargeStatus.Refunded);
    }

    [Fact]
    public void MarkOverdue_IsIdempotent()
    {
        var charge = NewCharge();

        charge.MarkOverdue();
        charge.MarkOverdue();

        charge.Status.Should().Be(ChargeStatus.Overdue);
    }
}
