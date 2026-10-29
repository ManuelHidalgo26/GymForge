using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Settings;
using GymForge.Desktop.ViewModels.Shared;
using GymForge.Domain.Entities;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Configuración → "Cobranzas y recordatorios": editar las 5 etapas y el cobro automático.</summary>
public class SettingsViewModelDunningTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly DunningConfig _dunning = new();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");

    private async Task<SettingsViewModel> SutAsync()
    {
        var session = await TestSession.InitializedAsync(_company, [], _dunning);
        var vm = new SettingsViewModel(
            _mediator, Substitute.For<ISiteRepository>(), Substitute.For<IMemberRepository>(), session,
            new GatekeeperConfig(), new CurrentLicense(), Substitute.For<IDataTransfer>(),
            Substitute.For<IDatabaseBackup>(), Substitute.For<ILocalSettings>(),
            Substitute.For<IMachineFingerprint>(), new UpdateViewModel(Substitute.For<IUpdateService>()),
            _dunning);
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Load_MuestraLaConfiguracionVigenteDelCobro()
    {
        _dunning.Enabled = true;
        _dunning.Stages =
        [
            new DunningStage(10, "Segunda", "Texto B"),
            new DunningStage(2, "Primera", "Texto A"),
        ];

        var sut = await SutAsync();

        sut.DunningEnabled.Should().BeTrue();
        sut.DunningStages.Select(s => (s.DaysOverdue, s.Label, s.Template))
            .Should().Equal((2, "Primera", "Texto A"), (10, "Segunda", "Texto B"));
    }

    [Fact]
    public async Task Load_ConLosDefaultsDeFabrica_MuestraLasCincoEtapasConElCobroApagado()
    {
        var sut = await SutAsync();

        sut.DunningEnabled.Should().BeFalse();
        sut.DunningStages.Should().HaveCount(5);
        sut.DunningStages.Select(s => s.DaysOverdue).Should().Equal(1, 3, 7, 15, 30);
    }

    [Fact]
    public async Task Save_EnviaElComandoConLoEditado_YAvisaQueSeGuardo()
    {
        var sut = await SutAsync();
        sut.DunningEnabled = true;
        sut.DunningStages[0].DaysOverdue = 2;
        sut.DunningStages[0].Label = "Aviso editado";
        sut.DunningStages[0].Template = "Hola {nombre}, nueva plantilla";

        await sut.SaveDunningCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<UpdateDunningSettingsCommand>(c =>
                c.CompanyId == _company.Id && c.Enabled && c.Stages.Count == 5 &&
                c.Stages[0] == new DunningStageInput(2, "Aviso editado", "Hola {nombre}, nueva plantilla") &&
                c.Stages[4].DaysOverdue == 30),
            Arg.Any<CancellationToken>());
        sut.DunningSaved.Should().BeTrue();
        sut.DunningMessage.Should().Be("Cobranzas guardadas.");
    }

    [Fact]
    public async Task Save_SiLaValidacionRechaza_MuestraLosMotivos_SinMarcarGuardado()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<UpdateDunningSettingsCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ValidationException([
                new ValidationFailure("Stages", "Los días de vencido no se pueden repetir entre etapas."),
                new ValidationFailure("Stages", "El mensaje de cada etapa es obligatorio.")]));

        await sut.SaveDunningCommand.ExecuteAsync(null);

        sut.DunningSaved.Should().BeFalse();
        sut.DunningMessage.Should().Contain("no se pueden repetir").And.Contain("obligatorio");
    }

    [Fact]
    public async Task Save_SiFallaOtraCosa_MuestraElError_SinLanzar()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<UpdateDunningSettingsCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disco lleno"));

        await sut.SaveDunningCommand.ExecuteAsync(null);

        sut.DunningSaved.Should().BeFalse();
        sut.DunningMessage.Should().Contain("disco lleno");
    }

    [Fact]
    public async Task Save_UnGuardadoExitosoPosteriorLimpiaElErrorAnterior()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<UpdateDunningSettingsCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disco lleno"));
        await sut.SaveDunningCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<UpdateDunningSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await sut.SaveDunningCommand.ExecuteAsync(null);

        sut.DunningSaved.Should().BeTrue();
        sut.DunningMessage.Should().Be("Cobranzas guardadas.");
    }
}
