using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.ViewModels.Settings;
using GymForge.Desktop.ViewModels.Shared;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Configuración → "Origen de las actualizaciones": se valida primero, luego se pide el PIN y recién ahí se guarda.</summary>
public class SettingsViewModelUpdateFeedTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ILocalSettings _settings = Substitute.For<ILocalSettings>();

    private async Task<SettingsViewModel> SutAsync()
    {
        var dunning = new DunningConfig();
        var session = await TestSession.InitializedAsync(Company.Create("Power Gym", "30-71234567-3"), [], dunning);
        return new SettingsViewModel(
            _mediator, Substitute.For<ISiteRepository>(), Substitute.For<IMemberRepository>(), session,
            new GatekeeperConfig(), new CurrentLicense(), Substitute.For<IDataTransfer>(),
            Substitute.For<IDatabaseBackup>(), _settings,
            Substitute.For<IMachineFingerprint>(), new UpdateViewModel(Substitute.For<IUpdateService>()), dunning,
            Substitute.For<IBackupKeyStore>());
    }

    private void PinIs(bool valid) =>
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>())
            .Returns(valid ? new StaffDto(Guid.NewGuid(), "Admin", StaffRole.Admin, "#112233") : null);

    private static async Task SaveAsync(SettingsViewModel vm, string input, string pin = "1234")
    {
        vm.UpdateFeedInput = input;
        vm.UpdateFeedPin = pin;
        await vm.SaveUpdateFeedCommand.ExecuteAsync(null);
    }

    [Theory]
    [InlineData("https://updates.example.com/releases")]
    [InlineData(@"\\servidor\share")]
    [InlineData(@"C:\releases")]
    public async Task OrigenValidoConPinCorrecto_SePersiste(string input)
    {
        PinIs(true);
        var vm = await SutAsync();

        await SaveAsync(vm, $"  {input}  ");

        _settings.UpdateFeedUrl.Should().Be(input);
        _settings.Received(1).Save();
        vm.UpdateFeedSaved.Should().BeTrue();
        vm.UpdateFeedPin.Should().BeEmpty();
        vm.UpdateFeedDisplay.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PinIncorrecto_NoSePersiste()
    {
        PinIs(false);
        var vm = await SutAsync();

        await SaveAsync(vm, "https://updates.example.com", pin: "0000");

        vm.UpdateFeedMessage.Should().Be("PIN incorrecto.");
        vm.UpdateFeedSaved.Should().BeFalse();
        _settings.DidNotReceive().Save();
    }

    [Fact]
    public async Task OrigenHttp_SeRechazaAntesDePedirElPin()
    {
        var vm = await SutAsync();

        await SaveAsync(vm, "http://updates.example.com");

        vm.UpdateFeedMessage.Should().Contain("https://");
        vm.UpdateFeedSaved.Should().BeFalse();
        await _mediator.DidNotReceive().Send(Arg.Any<AuthenticateStaffCommand>());
        _settings.DidNotReceive().Save();
    }

    [Fact]
    public async Task OrigenVacioConPinCorrecto_BorraElOverride()
    {
        PinIs(true);
        _settings.UpdateFeedUrl.Returns("https://viejo.example.com");
        var vm = await SutAsync();

        await SaveAsync(vm, "  ");

        _settings.UpdateFeedUrl.Should().BeNull();
        _settings.Received(1).Save();
    }
}
