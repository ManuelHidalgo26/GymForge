using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class UpdateDunningSettingsCommandTests
{
    private readonly ISiteRepository _repo = Substitute.For<ISiteRepository>();
    private readonly DunningConfig _config = new();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");

    public UpdateDunningSettingsCommandTests()
    {
        _repo.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
    }

    private UpdateDunningSettingsCommandHandler Handler() => new(_repo, _config);

    private UpdateDunningSettingsCommand Command(bool enabled = true, params DunningStageInput[] stages) =>
        new(_company.Id, enabled, stages.Length > 0 ? stages : [new DunningStageInput(3, "Aviso", "Hola {nombre}")]);

    [Fact]
    public async Task PersisteElJsonEnLaEmpresa_YActualizaElSingleton()
    {
        var command = Command(true,
            new DunningStageInput(10, "Firme", "{nombre}, debés {monto}"),
            new DunningStageInput(3, "Aviso", "Hola {nombre}"));

        await Handler().Handle(command, CancellationToken.None);

        var persisted = DunningSettings.FromJson(_company.DunningJson);
        persisted.Should().NotBeNull();
        persisted!.Enabled.Should().BeTrue();
        persisted.Stages.Select(s => s.DaysOverdue).Should().Equal(3, 10);   // quedan ordenadas por días

        _config.Enabled.Should().BeTrue();
        _config.Stages.Should().Equal(
            new DunningStage(3, "Aviso", "Hola {nombre}"),
            new DunningStage(10, "Firme", "{nombre}, debés {monto}"));
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecortaEspaciosDeLabelYPlantilla()
    {
        await Handler().Handle(
            Command(false, new DunningStageInput(3, "  Aviso  ", "  Hola {nombre}  ")), CancellationToken.None);

        _config.Stages.Should().Equal(new DunningStage(3, "Aviso", "Hola {nombre}"));
        _config.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task EmpresaInexistente_Lanza_YNoTocaElSingleton()
    {
        var unknown = new UpdateDunningSettingsCommand(
            Guid.NewGuid(), true, [new DunningStageInput(3, "Aviso", "Hola")]);

        var act = () => Handler().Handle(unknown, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _config.Enabled.Should().BeFalse();
        _config.Stages.Should().BeEquivalentTo(DunningConfig.DefaultStages);
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiElGuardadoFalla_ElSingletonNoCambia()
    {
        _repo.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns<int>(_ => throw new IOException("disco lleno"));

        var act = () => Handler().Handle(Command(true), CancellationToken.None);

        await act.Should().ThrowAsync<IOException>();
        _config.Enabled.Should().BeFalse("no se aplica en caliente algo que no quedó persistido");
    }

    // ── Validación ───────────────────────────────────────────────────────────

    private static readonly UpdateDunningSettingsCommandValidator Validator = new();

    private static string[] ErrorsOf(UpdateDunningSettingsCommand command) =>
        Validator.Validate(command).Errors.Select(e => e.ErrorMessage).ToArray();

    [Fact]
    public void Validator_AceptaUnaConfiguracionValida()
    {
        ErrorsOf(Command(true,
            new DunningStageInput(1, "Aviso", "Hola {nombre}"),
            new DunningStageInput(7, "Firme", "{nombre}, debés {monto}"))).Should().BeEmpty();
    }

    [Fact]
    public void Validator_RechazaDiasRepetidos()
    {
        ErrorsOf(Command(true,
            new DunningStageInput(3, "Uno", "Hola"),
            new DunningStageInput(3, "Dos", "Chau"))).Should().ContainSingle().Which.Should().Contain("repetir");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validator_RechazaDiasQueNoSonMora(int days)
    {
        ErrorsOf(Command(true, new DunningStageInput(days, "Aviso", "Hola"))).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validator_RechazaPlantillaVacia(string template)
    {
        ErrorsOf(Command(true, new DunningStageInput(3, "Aviso", template))).Should().NotBeEmpty();
    }

    [Fact]
    public void Validator_RechazaPlantillaMuyLarga()
    {
        ErrorsOf(Command(true, new DunningStageInput(3, "Aviso", new string('x', 1001)))).Should().NotBeEmpty();
        ErrorsOf(Command(true, new DunningStageInput(3, "Aviso", new string('x', 1000)))).Should().BeEmpty();
    }

    [Fact]
    public void Validator_RechazaLabelVacio()
    {
        ErrorsOf(Command(true, new DunningStageInput(3, " ", "Hola"))).Should().NotBeEmpty();
    }

    [Fact]
    public void Validator_ExigeAlMenosUnaEtapa()
    {
        var sinEtapas = new UpdateDunningSettingsCommand(_company.Id, true, []);

        ErrorsOf(sinEtapas).Should().NotBeEmpty();
    }
}
