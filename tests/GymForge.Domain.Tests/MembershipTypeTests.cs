using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class MembershipTypeTests
{
    private static MembershipType PlanOf(int durationValue, string durationUnit) =>
        MembershipType.Create(
            Guid.NewGuid(), "Plan", MembershipBasis.Renewal, 35_000m, durationValue, durationUnit);

    [Fact]
    public void CalculateEndDate_OneMonth_AddsOneMonth()
    {
        var start = new DateOnly(2026, 3, 15);

        PlanOf(1, "Month").CalculateEndDate(start).Should().Be(new DateOnly(2026, 4, 15));
    }

    [Fact]
    public void CalculateEndDate_TwelveMonths_AddsOneYear()
    {
        var start = new DateOnly(2026, 3, 15);

        PlanOf(12, "Month").CalculateEndDate(start).Should().Be(new DateOnly(2027, 3, 15));
    }

    [Fact]
    public void CalculateEndDate_MonthEnd_ClampsToLastDayOfShorterMonth()
    {
        var start = new DateOnly(2026, 1, 31);

        PlanOf(1, "Month").CalculateEndDate(start).Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void CalculateEndDate_Days_AddsDays()
    {
        var start = new DateOnly(2026, 3, 15);

        PlanOf(7, "Day").CalculateEndDate(start).Should().Be(new DateOnly(2026, 3, 22));
    }

    [Fact]
    public void CalculateEndDate_Year_AddsYears()
    {
        var start = new DateOnly(2026, 3, 15);

        PlanOf(2, "Year").CalculateEndDate(start).Should().Be(new DateOnly(2028, 3, 15));
    }

    [Fact]
    public void CalculateEndDate_UnknownUnit_FallsBackToMonths()
    {
        var start = new DateOnly(2026, 3, 15);

        PlanOf(3, "Quarter").CalculateEndDate(start).Should().Be(new DateOnly(2026, 6, 15));
    }
}
