using GymForge.Application;
using GymForge.Application.UseCases.Access;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Desktop.ViewModels.Checkin;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.Services;
using GymForge.Desktop.Views.Shell;
using GymForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.IO;

namespace GymForge.Desktop;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGymForgeDesktop(this IServiceCollection services)
    {
        // Database: %LOCALAPPDATA%\GymForge\gymforge.db
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GymForge");
        Directory.CreateDirectory(dbDir);
        var connectionString =
            $"Data Source={Path.Combine(dbDir, "gymforge.db")};Mode=ReadWriteCreate;Cache=Shared";

        // Logging + Application + Infrastructure
        // Conecta Microsoft.Extensions.Logging al Serilog estático (seeder, repos, etc.)
        services.AddLogging(builder => builder.AddSerilog(dispose: false));
        services.AddApplication();
        services.AddInfrastructure(connectionString);

        // Gatekeeper
        services.AddSingleton<GatekeeperConfig>();
        services.AddScoped<ValidateSwipeUseCase>();

        // ── Session ───────────────────────────────────────────────────────────
        // Singleton: tenant + sede activa + cajero + turno de caja en curso
        services.AddSingleton<SessionContext>();

        // ── Shell ─────────────────────────────────────────────────────────────
        // Singleton: owns the navigation router + VM cache
        services.AddSingleton<MainWindowViewModel>(sp => new MainWindowViewModel(sp));

        // ── Dashboard ─────────────────────────────────────────────────────────
        services.AddSingleton<DashboardViewModel>();

        // ── Members ───────────────────────────────────────────────────────────
        // Singleton: preserves list scroll/search state across navigations
        services.AddSingleton<MembersListViewModel>();
        // Transient: each open of the detail/create form is a fresh instance
        services.AddTransient<MemberDetailViewModel>();
        services.AddTransient<CreateMemberViewModel>();

        // ── Check-in Kiosk ────────────────────────────────────────────────────
        // Transient: always resets state when navigated to
        services.AddTransient<CheckInKioskViewModel>();

        // ── Charges ───────────────────────────────────────────────────────────
        // Transient: the router caches the site-wide instance via _vmCache;
        // MemberDetailViewModel also gets a fresh one (per-member filter).
        services.AddTransient<ChargesViewModel>();

        // ── Cash register ─────────────────────────────────────────────────────
        services.AddTransient<CashViewModel>();

        return services;
    }
}
