using FluentAssertions;
using GymForge.Application.UseCases.Dunning;

namespace GymForge.Application.Tests.Dunning;

public class DunningMessageRendererTests
{
    private static readonly DunningContext Ctx =
        new("Ana", "PowerGym", 35_000m, new DateOnly(2026, 7, 21), DaysOverdue: 7);

    [Fact]
    public void Render_SustituyeTodosLosPlaceholders()
    {
        var body = DunningMessageRenderer.Render(
            "Hola {nombre}, tu cuota de {gimnasio} lleva {dias} días (saldo {monto}, venció el {vencimiento}).", Ctx);

        body.Should().Be("Hola Ana, tu cuota de PowerGym lleva 7 días (saldo $35.000, venció el 21/07/2026).");
    }

    [Fact]
    public void Render_FormateaMontoYFechaEnEsAr()
    {
        var ctx = Ctx with { Outstanding = 1_250_500m, DueDate = new DateOnly(2026, 1, 5) };

        var body = DunningMessageRenderer.Render("{monto} / {vencimiento}", ctx);

        body.Should().Be("$1.250.500 / 05/01/2026");
    }

    [Fact]
    public void Render_PlaceholderDesconocido_QuedaLiteral()
    {
        var body = DunningMessageRenderer.Render("Hola {nombre}, {saludo} y {Nombre}", Ctx);

        body.Should().Be("Hola Ana, {saludo} y {Nombre}");
    }

    [Fact]
    public void Render_RepiteUnPlaceholderLasVecesQueAparezca()
    {
        var body = DunningMessageRenderer.Render("{nombre} {nombre}", Ctx);

        body.Should().Be("Ana Ana");
    }

    [Fact]
    public void Render_SinPlaceholders_DevuelveElTextoTalCual()
    {
        DunningMessageRenderer.Render("Pasá por recepción.", Ctx).Should().Be("Pasá por recepción.");
    }

    [Fact]
    public void DefaultStages_SonCincoYUsanSoloPlaceholdersSoportados()
    {
        DunningConfig.DefaultStages.Select(s => s.DaysOverdue).Should().Equal(1, 3, 7, 15, 30);

        foreach (var stage in DunningConfig.DefaultStages)
        {
            var body = DunningMessageRenderer.Render(stage.Template, Ctx);

            body.Should().NotContain("{", because: $"la etapa '{stage.Label}' no debe dejar placeholders sin resolver");
            body.Should().Contain("Ana").And.Contain("PowerGym").And.Contain("$35.000");
        }
    }
}
