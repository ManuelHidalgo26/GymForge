using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Settings;
using GymForge.Desktop.ViewModels.Shared;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Configuración → "Frase de acceso del respaldo": se fija con PIN, nunca se muestra y exige un mínimo.</summary>
public class SettingsViewModelBackupPassphraseTests
{
    private const string Phrase = "frase de acceso bien larga";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IBackupKeyStore _keys = Substitute.For<IBackupKeyStore>();

    private async Task<SettingsViewModel> SutAsync()
    {
        var dunning = new DunningConfig();
        var session = await TestSession.InitializedAsync(Company.Create("Power Gym", "30-71234567-3"), [], dunning);
        return new SettingsViewModel(
            _mediator, Substitute.For<ISiteRepository>(), Substitute.For<IMemberRepository>(), session,
            new GatekeeperConfig(), new CurrentLicense(), Substitute.For<IDataTransfer>(),
            Substitute.For<IDatabaseBackup>(), Substitute.For<ILocalSettings>(),
            Substitute.For<IMachineFingerprint>(), new UpdateViewModel(Substitute.For<IUpdateService>()), dunning,
            _keys);
    }

    private static void Fill(SettingsViewModel vm, string phrase, string confirm, string pin = "1234")
    {
        vm.PassphraseInput = phrase;
        vm.PassphraseConfirm = confirm;
        vm.PassphrasePin = pin;
    }

    [Fact]
    public async Task FraseCorta_SeRechazaSinPedirElPinNiGuardar()
    {
        var vm = await SutAsync();
        Fill(vm, "corta", "corta");

        await vm.SetBackupPassphraseCommand.ExecuteAsync(null);

        vm.PassphraseOk.Should().BeFalse();
        vm.PassphraseMessage.Should().Contain("al menos 12");
        _keys.DidNotReceive().SetPassphrase(Arg.Any<string>());
        await _mediator.DidNotReceive().Send(Arg.Any<AuthenticateStaffCommand>());
    }

    [Fact]
    public async Task FrasesQueNoCoinciden_NoSeGuarda()
    {
        var vm = await SutAsync();
        Fill(vm, Phrase, Phrase + "x");

        await vm.SetBackupPassphraseCommand.ExecuteAsync(null);

        vm.PassphraseMessage.Should().Contain("no coinciden");
        _keys.DidNotReceive().SetPassphrase(Arg.Any<string>());
    }

    [Fact]
    public async Task PinIncorrecto_NoSeGuarda()
    {
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>()).Returns((StaffDto?)null);
        var vm = await SutAsync();
        Fill(vm, Phrase, Phrase, pin: "0000");

        await vm.SetBackupPassphraseCommand.ExecuteAsync(null);

        vm.PassphraseMessage.Should().Be("PIN incorrecto.");
        _keys.DidNotReceive().SetPassphrase(Arg.Any<string>());
    }

    [Fact]
    public async Task ConPinCorrecto_GuardaLaFrase_LimpiaLosCamposYAvisaDeLaPerdidaIrreversible()
    {
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>()).Returns(new StaffDto(Guid.NewGuid(), "Admin", StaffRole.Admin, "#112233"));
        var vm = await SutAsync();
        Fill(vm, Phrase, Phrase);

        await vm.SetBackupPassphraseCommand.ExecuteAsync(null);

        _keys.Received(1).SetPassphrase(Phrase);
        vm.PassphraseOk.Should().BeTrue();
        vm.HasBackupPassphrase.Should().BeTrue();
        vm.BackupPassphraseActionText.Should().Be("Cambiar frase");
        vm.PassphraseMessage.Should().Contain("si la perdés").And.NotContain(Phrase);
        vm.PassphraseInput.Should().BeEmpty();
        vm.PassphraseConfirm.Should().BeEmpty();
        vm.PassphrasePin.Should().BeEmpty();
    }
}
