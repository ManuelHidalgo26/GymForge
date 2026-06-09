using Avalonia;
using Avalonia.Fonts.Inter;
using GymForge.Desktop;
using GymForge.Desktop.Services;
using GymForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/gymforge-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("GymForge starting up");

    var services = new ServiceCollection();
    services.AddGymForgeDesktop();
    App.Services = services.BuildServiceProvider();

    // Migrate + seed on startup
    await App.Services.InitialiseDatabaseAsync();

    // Resolver tenant + sedes reales (reemplaza los GUID hardcodeados)
    await App.Services.GetRequiredService<SessionContext>().InitializeAsync();

    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
}
catch (Exception ex)
{
    Log.Fatal(ex, "GymForge terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

static AppBuilder BuildAvaloniaApp() =>
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .LogToTrace();
