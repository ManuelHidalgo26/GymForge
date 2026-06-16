using Avalonia;
using Avalonia.Fonts.Inter;
using GymForge.Desktop;
using GymForge.Desktop.Services;
using GymForge.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.Globalization;
using Velopack;

// Velopack: maneja los hooks de instalación/actualización (al correr el Setup.exe
// o tras actualizar). En una ejecución normal no hace nada y sigue de largo.
VelopackApp.Build().Run();

// Moneda y fechas en formato AR ($35.000,00) sin depender de la config regional
// de la máquina (en una Windows en-US mostraría US$).
var culture = new CultureInfo("es-AR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

// Logs en %LOCALAPPDATA%\GymForge\logs: el exe publicado puede estar en una
// carpeta sin permisos de escritura (Descargas, Program Files).
var logDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "GymForge", "logs");
Directory.CreateDirectory(logDir);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // EF Core loguea cada comando SQL en Information: lo bajamos a Warning.
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(logDir, "gymforge-.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("GymForge starting up");

    var services = new ServiceCollection();
    services.AddGymForgeDesktop();
    App.Services = services.BuildServiceProvider();

    // Migrate + seed on startup
    await App.Services.InitialiseDatabaseAsync();
    Log.Information("Database migrated and seeded");

    // Resolver tenant + sedes reales (reemplaza los GUID hardcodeados)
    await App.Services.GetRequiredService<SessionContext>().InitializeAsync();
    Log.Information("Session initialised — UI starting");

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
