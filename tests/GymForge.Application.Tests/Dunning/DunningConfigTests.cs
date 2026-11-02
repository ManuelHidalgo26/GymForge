using FluentAssertions;
using GymForge.Application.UseCases.Dunning;

namespace GymForge.Application.Tests.Dunning;

public class DunningConfigTests
{
    private readonly DunningConfig _config = new();   // etapas de fábrica: 1, 3, 7, 15 y 30

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 3)]
    [InlineData(6, 3)]
    [InlineData(7, 7)]
    [InlineData(20, 15)]
    [InlineData(30, 30)]
    [InlineData(400, 30)]
    public void CurrentStageFor_EsLaDeMayorAntiguedadYaCumplida(int daysOverdue, int expectedStage)
    {
        _config.CurrentStageFor(daysOverdue)!.DaysOverdue.Should().Be(expectedStage);
    }

    [Fact]
    public void CurrentStageFor_NoDependeDelOrdenEnQueSeGuardaron()
    {
        _config.Stages = [new DunningStage(30, "Ultima", "x"), new DunningStage(3, "Primera", "y")];

        _config.CurrentStageFor(10)!.Label.Should().Be("Primera");
    }

    [Fact]
    public void CurrentStageFor_AntesDeLaPrimeraEtapa_DevuelveNull()
    {
        _config.Stages = [new DunningStage(3, "Primera", "y")];

        _config.CurrentStageFor(2).Should().BeNull();
    }

    [Fact]
    public void FirstStage_EsLaDeMenorAntiguedad()
    {
        _config.Stages = [new DunningStage(30, "Ultima", "x"), new DunningStage(3, "Primera", "y")];

        _config.FirstStage.Label.Should().Be("Primera");
    }
}
