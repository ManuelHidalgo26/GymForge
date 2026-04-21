using Avalonia;
using GymForge.Desktop;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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

    // Ensure DB is created and migrated on startup
    using (var scope = App.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
        db.Database.Migrate();
    }

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
        .UseInterFont()
        .LogToTrace();
