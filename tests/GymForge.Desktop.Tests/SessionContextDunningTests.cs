using FluentAssertions;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;

namespace GymForge.Desktop.Tests;

/// <summary>Al arrancar, la sesión hidrata el singleton del cobro automático desde <c>Company.DunningJson</c>.</summary>
public class SessionContextDunningTests
{
    private static async Task<DunningConfig> InitializeWithAsync(string? dunningJson)
    {
        var company = Company.Create("Power Gym", "30-71234567-3");
        if (dunningJson is not null)
            company.SetDunningConfig(dunningJson);

        var dunning = new DunningConfig();
        await TestSession.InitializedAsync(company, [], dunning);
        return dunning;
    }

    [Fact]
    public async Task Initialize_HidrataElSingletonDesdeLaEmpresa()
    {
        var saved = new DunningSettings(true, [new DunningStageSettings(5, "Propia", "Hola {nombre}")]);

        var dunning = await InitializeWithAsync(saved.ToJson());

        dunning.Enabled.Should().BeTrue();
        dunning.Stages.Should().Equal(new DunningStage(5, "Propia", "Hola {nombre}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{json roto")]
    public async Task Initialize_SinConfigUtil_DejaLosDefaultsDeFabrica(string? json)
    {
        var dunning = await InitializeWithAsync(json);

        dunning.Enabled.Should().BeFalse();
        dunning.Stages.Should().BeEquivalentTo(DunningConfig.DefaultStages);
    }
}
