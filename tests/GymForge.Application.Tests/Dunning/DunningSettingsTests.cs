using FluentAssertions;
using GymForge.Application.UseCases.Dunning;

namespace GymForge.Application.Tests.Dunning;

public class DunningSettingsTests
{
    [Fact]
    public void RoundTrip_ToJson_FromJson_ConservaTodo()
    {
        var settings = new DunningSettings(true,
        [
            new DunningStageSettings(2, "Aviso", "Hola {nombre} ñandú ¡gracias!"),
            new DunningStageSettings(10, "Firme", "{nombre}, debés {monto}"),
        ]);

        var restored = DunningSettings.FromJson(settings.ToJson());

        restored.Should().NotBeNull();
        restored!.Enabled.Should().BeTrue();
        restored.Stages.Should().Equal(settings.Stages);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FromJson_SinDatos_DevuelveNull(string? json)
    {
        DunningSettings.FromJson(json).Should().BeNull();
    }

    [Theory]
    [InlineData("{no es json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"texto\"")]
    public void FromJson_Corrupto_DevuelveNullSinLanzar(string json)
    {
        var act = () => DunningSettings.FromJson(json);

        act.Should().NotThrow();
        act().Should().BeNull();
    }

    [Fact]
    public void FromJson_SinEtapas_DevuelveNull()
    {
        DunningSettings.FromJson("""{"Enabled":true}""").Should().BeNull();
        DunningSettings.FromJson("""{"Enabled":true,"Stages":null}""").Should().BeNull();
        DunningSettings.FromJson("""{"Enabled":true,"Stages":[]}""").Should().BeNull();
    }

    [Fact]
    public void Default_TraeLasCincoEtapasDeFabrica_ConElCobroApagado()
    {
        var settings = DunningSettings.Default();

        settings.Enabled.Should().BeFalse();
        settings.Stages.Should().HaveCount(5);
        settings.Stages.Select(s => (s.DaysOverdue, s.Label, s.Template))
            .Should().Equal(DunningConfig.DefaultStages.Select(s => (s.DaysOverdue, s.Label, s.Template)));
    }

    [Fact]
    public void ApplyTo_ActualizaElSingletonEnCaliente()
    {
        var config = new DunningConfig();
        var settings = new DunningSettings(true, [new DunningStageSettings(4, "Propia", "Hola {nombre}")]);

        settings.ApplyTo(config);

        config.Enabled.Should().BeTrue();
        config.Stages.Should().Equal(new DunningStage(4, "Propia", "Hola {nombre}"));
    }
}
