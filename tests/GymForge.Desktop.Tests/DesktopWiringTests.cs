using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Reports;
using GymForge.Desktop.ViewModels.Settings;
using GymForge.Desktop.ViewModels.Shared;
using GymForge.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Desktop.Tests;

/// <summary>El host de escritorio arma estas piezas por DI: acá se verifica que se resuelvan y se compartan.</summary>
public class DesktopWiringTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddGymForgeDesktop("Data Source=:memory:");
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Updates_ResolveAsASingletonSharedByTheShellAndSettings()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IUpdateService>().Should().BeOfType<VelopackUpdateService>();
        var updates = provider.GetRequiredService<UpdateViewModel>();

        updates.Should().BeSameAs(provider.GetRequiredService<UpdateViewModel>());
        provider.GetRequiredService<SettingsViewModel>().Updates.Should().BeSameAs(updates);
    }

    [Fact]
    public void SettingsViewModel_ResolvesWithTheMachineFingerprint()
    {
        using var provider = BuildProvider();

        var settings = provider.GetRequiredService<SettingsViewModel>();

        settings.Should().NotBeNull();
        provider.GetRequiredService<IMachineFingerprint>().Current().Should().NotBeEmpty();
    }

    [Fact]
    public void Reports_ResolveWithTheExporterAndTheSaveDialogService()
    {
        using var provider = BuildProvider();

        provider.GetRequiredService<IReportExporter>().Should().BeOfType<ReportExporter>();
        provider.GetRequiredService<IReportExportService>().Should().BeOfType<ReportExportService>();
        provider.GetRequiredService<ReportsViewModel>().Should().NotBeNull();
    }

    [Fact]
    public void EveryRegisteredService_HasResolvableConstructorDependencies()
    {
        var services = new ServiceCollection();
        services.AddGymForgeDesktop("Data Source=:memory:");

        var build = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }).Dispose();

        build.Should().NotThrow();
    }
}
