using GymForge.Application;
using GymForge.Application.UseCases.Access;
using GymForge.Desktop.ViewModels.Checkin;
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

        // Shell ViewModels (singleton — live for app lifetime)
        services.AddSingleton<MainWindowViewModel>();

        // Feature ViewModels (transient — new instance per navigation)
        services.AddTransient<MembersListViewModel>();
        services.AddTransient<CreateMemberViewModel>();
        services.AddTransient<CheckInKioskViewModel>();

        return services;
    }
}
