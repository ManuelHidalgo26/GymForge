using FluentAssertions;
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

namespace GymForge.Desktop.Tests;

/// <summary>Configuración → "Copia externa": aviso de copias viejas en claro y borrado solo tras confirmar.</summary>
public class SettingsViewModelLegacySweepTests
{
    private static async Task<SettingsViewModel> SutAsync(IDatabaseBackup backup)
    {
        var dunning = new DunningConfig();
        var session = await TestSession.InitializedAsync(Company.Create("Power Gym", "30-71234567-3"), [], dunning);
        var vm = new SettingsViewModel(
            Substitute.For<IMediator>(), Substitute.For<ISiteRepository>(), Substitute.For<IMemberRepository>(), session,
            new GatekeeperConfig(), new CurrentLicense(), Substitute.For<IDataTransfer>(),
            backup, Substitute.For<ILocalSettings>(), Substitute.For<IMachineFingerprint>(),
            new UpdateViewModel(Substitute.For<IUpdateService>()), dunning, Substitute.For<IBackupKeyStore>());
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Load_ConCopiasEnClaro_MuestraElAvisoSinBorrarNada()
    {
        var backup = Substitute.For<IDatabaseBackup>();
        backup.FindLegacyPlainOffsiteCopies().Returns(["a.db", "b.db"]);

        var vm = await SutAsync(backup);

        vm.HasLegacyPlainCopies.Should().BeTrue();
        vm.LegacyPlainCopiesText.Should().Be("Hay 2 copias viejas sin cifrar en el destino externo.");
        backup.DidNotReceive().DeleteLegacyPlainOffsiteCopies();
    }

    [Fact]
    public async Task Borrar_PideConfirmacionYSoloBorraAlConfirmar()
    {
        var backup = Substitute.For<IDatabaseBackup>();
        backup.FindLegacyPlainOffsiteCopies().Returns(["a.db"]);
        backup.DeleteLegacyPlainOffsiteCopies().Returns(["a.db"]);
        var vm = await SutAsync(backup);

        backup.FindLegacyPlainOffsiteCopies().Returns([]);   // tras borrar, el destino ya no tiene copias en claro

        vm.AskLegacySweepCommand.Execute(null);
        vm.IsLegacySweepConfirming.Should().BeTrue();
        backup.DidNotReceive().DeleteLegacyPlainOffsiteCopies();

        await vm.ConfirmLegacySweepCommand.ExecuteAsync(null);

        backup.Received(1).DeleteLegacyPlainOffsiteCopies();
        vm.HasLegacyPlainCopies.Should().BeFalse();
        vm.IsLegacySweepConfirming.Should().BeFalse();
    }

    [Fact]
    public async Task Confirmar_ConBorradoParcial_InformaCuantasQuedaron()
    {
        var backup = Substitute.For<IDatabaseBackup>();
        backup.FindLegacyPlainOffsiteCopies().Returns(["a.db", "b.db", "c.db"]);
        backup.DeleteLegacyPlainOffsiteCopies().Returns(["a.db"]);
        var vm = await SutAsync(backup);

        vm.AskLegacySweepCommand.Execute(null);
        await vm.ConfirmLegacySweepCommand.ExecuteAsync(null);

        vm.OffsiteMessage.Should().Be(
            "Se borró 1 copia; 2 no se pudieron borrar (revisá que la carpeta esté disponible).");
        vm.OffsiteOk.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelar_NoBorraNada()
    {
        var backup = Substitute.For<IDatabaseBackup>();
        backup.FindLegacyPlainOffsiteCopies().Returns(["a.db"]);
        var vm = await SutAsync(backup);

        vm.AskLegacySweepCommand.Execute(null);
        vm.CancelLegacySweepCommand.Execute(null);

        vm.IsLegacySweepConfirming.Should().BeFalse();
        backup.DidNotReceive().DeleteLegacyPlainOffsiteCopies();
    }
}
