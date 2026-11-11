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

/// <summary>Configuración → "Código de esta PC": el "Copiado" aparece junto al botón y se apaga solo.</summary>
public class SettingsViewModelCopyFeedbackTests
{
    private static async Task<SettingsViewModel> SutAsync()
    {
        var company = Company.Create("Power Gym", "30-71234567-3");
        var dunning = new DunningConfig();
        var session = await TestSession.InitializedAsync(company, [], dunning);
        return new SettingsViewModel(
            Substitute.For<IMediator>(), Substitute.For<ISiteRepository>(), Substitute.For<IMemberRepository>(), session,
            new GatekeeperConfig(), new CurrentLicense(), Substitute.For<IDataTransfer>(),
            Substitute.For<IDatabaseBackup>(), Substitute.For<ILocalSettings>(),
            Substitute.For<IMachineFingerprint>(), new UpdateViewModel(Substitute.For<IUpdateService>()), dunning,
            Substitute.For<IBackupKeyStore>());
    }

    [Fact]
    public async Task ShowThisPcCodeCopied_TurnsOnTheInlineFeedbackAndThenOffAgain()
    {
        var vm = await SutAsync();
        vm.ThisPcCodeCopied.Should().BeFalse();

        var showing = vm.ShowThisPcCodeCopiedAsync(TimeSpan.FromMilliseconds(40));
        vm.ThisPcCodeCopied.Should().BeTrue();

        await showing;
        vm.ThisPcCodeCopied.Should().BeFalse();
    }

    [Fact]
    public async Task ShowThisPcCodeCopied_DoesNotTouchTheLicenseActivationMessage()
    {
        var vm = await SutAsync();

        await vm.ShowThisPcCodeCopiedAsync(TimeSpan.Zero);

        vm.LicenseMessage.Should().BeNull();
    }
}
