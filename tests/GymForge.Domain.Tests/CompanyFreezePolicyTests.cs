using FluentAssertions;
using GymForge.Domain.Entities;

namespace GymForge.Domain.Tests;

public class CompanyFreezePolicyTests
{
    private static Company NewCompany() => Company.Create("Gym Test", "30-12345678-9");

    [Fact]
    public void NewCompany_DefaultsTo90FreezeDaysPerYear() =>
        NewCompany().MaxFreezeDaysPerYear.Should().Be(90);

    [Fact]
    public void SetFreezePolicy_ChangesTheCap()
    {
        var company = NewCompany();

        company.SetFreezePolicy(45);

        company.MaxFreezeDaysPerYear.Should().Be(45);
    }

    [Fact]
    public void SetFreezePolicy_ZeroDisablesFreezing()
    {
        var company = NewCompany();

        company.SetFreezePolicy(0);

        company.MaxFreezeDaysPerYear.Should().Be(0);
    }

    [Fact]
    public void SetFreezePolicy_Negative_Throws()
    {
        var act = () => NewCompany().SetFreezePolicy(-1);

        act.Should().Throw<ArgumentException>();
    }
}
