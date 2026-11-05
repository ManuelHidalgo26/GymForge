using System.Globalization;
using FluentAssertions;
using GymForge.Desktop.ViewModels.Dashboard;

namespace GymForge.Desktop.Tests;

public class ChartAxisTests
{
    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");

    [Theory]
    [InlineData(1_480_000, 1_500_000, new[] { 0, 500_000, 1_000_000, 1_500_000 })]
    [InlineData(95_000, 100_000, new[] { 0, 50_000, 100_000 })]
    [InlineData(300, 300, new[] { 0, 100, 200, 300 })]
    [InlineData(7, 10, new[] { 0, 5, 10 })]
    [InlineData(1, 1, new[] { 0, 1 })]
    public void Build_RoundsTheTopUpToANiceNumberWithEvenlySpacedTicks(int max, int expectedTop, int[] expectedTicks)
    {
        var axis = ChartAxis.Build(max, EsAr);

        axis.Top.Should().Be(expectedTop);
        axis.Ticks.Select(t => t.Value).Should().Equal(expectedTicks.Select(v => (decimal)v));
    }

    [Fact]
    public void Build_NeverCutsTheMaximum()
    {
        foreach (var max in new[] { 1m, 3m, 11m, 99m, 101m, 250m, 1_234m, 87_654m, 1_000_001m })
            ChartAxis.Build(max, EsAr).Top.Should().BeGreaterThanOrEqualTo(max);
    }

    [Fact]
    public void Build_UsesAtMostFiveTicks()
    {
        foreach (var max in new[] { 1m, 6m, 13m, 77m, 301m, 4_999m, 12_345m, 987_654m })
            ChartAxis.Build(max, EsAr).Ticks.Count.Should().BeInRange(2, 5);
    }

    [Fact]
    public void Build_PositionsEachTickAsAFractionOfTheTop()
    {
        var axis = ChartAxis.Build(1_480_000, EsAr);

        var fractions = axis.Ticks.Select(t => t.Fraction).ToList();
        fractions.Should().HaveCount(4);
        fractions.Zip(new[] { 0.0, 1.0 / 3, 2.0 / 3, 1.0 })
            .Should().AllSatisfy(pair => pair.First.Should().BeApproximately(pair.Second, 1e-9));
    }

    [Fact]
    public void Build_FormatsTheLabelsAsArgentinePesos()
    {
        var axis = ChartAxis.Build(1_480_000, EsAr);

        axis.Ticks.Select(t => t.Label).Should().Equal("$0", "$500.000", "$1.000.000", "$1.500.000");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Build_WithoutRevenueDrawsOnlyTheZeroLine(int max)
    {
        var axis = ChartAxis.Build(max, EsAr);

        axis.Top.Should().Be(0);
        axis.Ticks.Should().ContainSingle().Which.Should().Be(new AxisTick(0m, 0.0, "$0"));
    }
}
