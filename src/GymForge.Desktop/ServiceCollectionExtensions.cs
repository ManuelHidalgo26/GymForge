using GymForge.Application;
using GymForge.Application.UseCases.Access;
using GymForge.Desktop.ViewModels.Checkin;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.Views.Shell;
using GymForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using System.IO;

namespace GymForge.Desktop;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGymForgeDesktop(this IServiceCollection services)
    {
        // Database path in local app data
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GymForge");
        Directory.CreateDirectory(dbDir);
        var connectionString = $"Data Source={Path.Combine(dbDir, "gymforge.db")};Mode=ReadWriteCreate;Cache=Shared";

        // Application + Infrastructure layers
        services.AddApplication();
        services.AddInfrastructure(connectionString);

        // Gatekeeper
        services.AddSingleton<GatekeeperConfig>();
        services.AddScoped<ValidateSwipeUseCase>();

        // Shell ViewModel — singleton, owns the navigation router cache
        // Receives IServiceProvider to resolve child VMs lazily
        services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(sp));

        // Feature ViewModels
        // Dashboard: singleton (cached by router, no stateful reset needed)
        services.AddSingleton<DashboardViewModel>();

        // Members: singleton (preserves search/scroll state across navigations)
        services.AddSingleton<MembersListViewModel>();
        services.AddTransient<CreateMemberViewModel>();

        // CheckIn: transient — router always requests a fresh instance to reset state
        services.AddTransient<CheckInKioskViewModel>();

        return services;
    }
}
