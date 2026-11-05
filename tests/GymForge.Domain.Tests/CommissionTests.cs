using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.ValueObjects;

namespace GymForge.Domain.Tests;

public class CommissionTests
{
    [Theory]
    [InlineData(35_000, 10, 3_500)]
    [InlineData(12_345, 7.5, 925.88)]      // 925,875 redondea hacia afuera
    [InlineData(1_000, 0, 0)]
    [InlineData(1_000, 100, 1_000)]
    [InlineData(0, 15, 0)]
    public void Calculate_AppliesThePercentageAndRoundsToCents(double baseAmount, double pct, double expected) =>
        Commission.Calculate((decimal)baseAmount, (decimal)pct).Should().Be((decimal)expected);

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void Calculate_RejectsAPercentageOutsideZeroToHundred(double pct)
    {
        var act = () => Commission.Calculate(1_000m, (decimal)pct);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Membership_SetCommission_SetsSellerAndAmount()
    {
        var membership = Membership.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1));
        var seller = Guid.NewGuid();

        membership.SetCommission(seller, 3_500m);

        membership.SoldByStaffId.Should().Be(seller);
        membership.CommissionAmount.Should().Be(3_500m);
    }

    [Fact]
    public void SaleLine_SetCommission_SetsSellerAndAmount()
    {
        var line = SaleLine.Create(Guid.NewGuid(), "Agua", 2, 1_500m);
        var seller = Guid.NewGuid();

        line.SetCommission(seller, 300m);

        line.CommissionStaffId.Should().Be(seller);
        line.CommissionAmount.Should().Be(300m);
    }

    [Fact]
    public void SetCommission_RejectsANegativeAmount()
    {
        var line = SaleLine.Create(Guid.NewGuid(), "Agua", 1, 1_500m);

        var act = () => line.SetCommission(Guid.NewGuid(), -1m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Staff_SetCommissions_StoresThePercentages()
    {
        var staff = Staff.Create(Guid.NewGuid(), "Caro", "Paz", StaffRole.Receptionist, "hash");

        staff.SetCommissions(10m, 20m, 5.5m);

        staff.CommissionPctMemberships.Should().Be(10m);
        staff.CommissionPctPt.Should().Be(20m);
        staff.CommissionPctProducts.Should().Be(5.5m);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 101, 0)]
    [InlineData(0, 0, 100.5)]
    public void Staff_SetCommissions_RejectsAnyPercentageOutOfRange_AndChangesNothing(double a, double b, double c)
    {
        var staff = Staff.Create(Guid.NewGuid(), "Caro", "Paz", StaffRole.Receptionist, "hash");
        staff.SetCommissions(10m, 10m, 10m);

        var act = () => staff.SetCommissions((decimal)a, (decimal)b, (decimal)c);

        act.Should().Throw<ArgumentOutOfRangeException>();
        staff.CommissionPctMemberships.Should().Be(10m);
        staff.CommissionPctProducts.Should().Be(10m);
    }

    [Fact]
    public void Product_SetCommissionPct_StoresItAndRejectsOutOfRange()
    {
        var product = Product.Create(Guid.NewGuid(), "SKU", "Barra", 1_000m);

        product.SetCommissionPct(12.5m);
        var act = () => product.SetCommissionPct(101m);

        product.CommissionStaffPct.Should().Be(12.5m);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
