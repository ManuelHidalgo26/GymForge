// GymForge.Api — Self-hosted Kestrel on localhost:5000
// Used for IPC between GymForge.Desktop and future mobile/web clients
using GymForge.Application;
using GymForge.Application.UseCases.Access;
using GymForge.Api.Endpoints;
using GymForge.Infrastructure;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/api-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();
    builder.WebHost.UseUrls("http://localhost:5000");

    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? $"Data Source={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymForge", "gymforge.db")}";

    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(connectionString);
    builder.Services.AddSingleton<GatekeeperConfig>();
    builder.Services.AddScoped<ValidateSwipeUseCase>();
    builder.Services.AddEndpointsApiExplorer();

    var app = builder.Build();

    await app.Services.InitialiseDatabaseAsync();

    // ── Endpoints ──────────────────────────────────────────────────────────────
    app.MapMemberEndpoints();
    app.MapMembershipEndpoints();
    app.MapAccessEndpoints();

    app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

    // Handle --seed-only flag for scripts/seed.ps1
    if (args.Contains("--seed-only"))
    {
        Log.Information("Seed-only mode: exiting after seed");
        return;
    }

    Log.Information("GymForge API listening on http://localhost:5000");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "API terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
