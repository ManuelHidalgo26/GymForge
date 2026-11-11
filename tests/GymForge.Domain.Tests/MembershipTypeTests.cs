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
    public void Create_VisitPackConVisitas_GuardaLaCantidad()
    {
        var plan = MembershipType.Create(Guid.NewGuid(), "Pack", MembershipBasis.VisitPack, 1m, visitsIncluded: 10);

        plan.VisitsIncluded.Should().Be(10);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Create_VisitPackSinVisitasValidas_Lanza(int? visits)
    {
        var act = () => MembershipType.Create(Guid.NewGuid(), "Pack", MembershipBasis.VisitPack, 1m, visitsIncluded: visits);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_PlanPorTiempoConVisitas_NormalizaANull()
    {
        var plan = MembershipType.Create(Guid.NewGuid(), "Mensual", MembershipBasis.Renewal, 1m, visitsIncluded: 5);

        plan.VisitsIncluded.Should().BeNull();
    }

    [Fact]
    public void InitialVisits_VisitPack_DevuelveLaCantidad()
    {
        var plan = MembershipType.Create(Guid.NewGuid(), "Pack", MembershipBasis.VisitPack, 1m, visitsIncluded: 8);

        plan.InitialVisits().Should().Be(8);
    }

    [Fact]
    public void InitialVisits_PlanPorTiempo_EsNull() =>
        PlanOf(1, "Month").InitialVisits().Should().BeNull();

    [Fact]
    public void InitialVisits_VisitPackLegadoSinVisitas_Lanza()
    {
        var plan = MembershipType.Create(Guid.NewGuid(), "Pack", MembershipBasis.VisitPack, 1m, visitsIncluded: 1);
        typeof(MembershipType).GetProperty(nameof(MembershipType.VisitsIncluded))!.SetValue(plan, null);

        var act = () => plan.InitialVisits();

        act.Should().Throw<InvalidOperationException>();
    }

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
