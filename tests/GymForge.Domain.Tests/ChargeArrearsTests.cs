using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

/// <summary>"En mora" = vencido a la fecha y con saldo pendiente: lo que mantiene al socio en Overdue.</summary>
public class ChargeArrearsTests
{
    private static readonly DateOnly DueDate = new(2026, 4, 1);

    private static Charge NewCharge() =>
        Charge.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota", 10_000m, DueDate);

    private static Charge InStatus(ChargeStatus status)
    {
        var charge = NewCharge();
        typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(charge, status);
        return charge;
    }

    [Fact]
    public void UnpaidChargeDueBeforeTheDate_IsInArrears() =>
        NewCharge().IsInArrears(DueDate.AddDays(1)).Should().BeTrue();

    [Fact]
    public void ChargeDueOnTheDate_IsNotInArrearsYet() =>
        NewCharge().IsInArrears(DueDate).Should().BeFalse();

    [Fact]
    public void ChargeDueAfterTheDate_IsNotInArrears() =>
        NewCharge().IsInArrears(DueDate.AddDays(-1)).Should().BeFalse();

    [Fact]
    public void PartiallyPaidCharge_StillInArrears()
    {
        var charge = NewCharge();
        charge.ApplyPayment(4_000m);

        charge.IsInArrears(DueDate.AddDays(1)).Should().BeTrue();
    }

    [Fact]
    public void FullyPaidCharge_IsNotInArrears()
    {
        var charge = NewCharge();
        charge.ApplyPayment(10_000m);

        charge.IsInArrears(DueDate.AddDays(30)).Should().BeFalse();
    }

    [Fact]
    public void OverdueStatusWithoutBalance_IsNotInArrears()
    {
        var charge = NewCharge();
        charge.MarkOverdue();
        charge.ApplyPayment(10_000m);

        charge.IsInArrears(DueDate.AddDays(30)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ChargeStatus.WrittenOff)]
    [InlineData(ChargeStatus.Refunded)]
    public void ClosedCharge_IsNotInArrearsEvenWithBalance(ChargeStatus status) =>
        InStatus(status).IsInArrears(DueDate.AddDays(30)).Should().BeFalse();

    [Fact]
    public void BilledChargeWithBalance_IsInArrears() =>
        InStatus(ChargeStatus.Billed).IsInArrears(DueDate.AddDays(1)).Should().BeTrue();
}
