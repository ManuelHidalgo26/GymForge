// GymForge.Api — Self-hosted Kestrel on localhost:5000
// Used for IPC between GymForge.Desktop and future mobile/web clients
using GymForge.Application;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Licensing;
using GymForge.Api.Endpoints;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
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
    // El DbContext filtra por empresa+sede: el tenant por defecto se fija abajo, tras migrar y sembrar.
    builder.Services.AddSingleton<AmbientTenantProvider>();
    builder.Services.AddSingleton<ICurrentTenant>(sp => sp.GetRequiredService<AmbientTenantProvider>());
    // Sin auth todavía (deuda D2): nadie fija el usuario, así que la auditoría registra operaciones de sistema.
    builder.Services.AddSingleton<AmbientCurrentUser>();
    builder.Services.AddSingleton<ICurrentUser>(sp => sp.GetRequiredService<AmbientCurrentUser>());
    builder.Services.AddInfrastructure(connectionString);
    builder.Services.AddSingleton<GatekeeperConfig>();
    builder.Services.AddScoped<ValidateSwipeUseCase>();
    builder.Services.AddEndpointsApiExplorer();

    var app = builder.Build();

    await app.Services.InitialiseDatabaseAsync();

    // Empresa por defecto (igual que el escritorio): fija el tenant de los filtros globales
    // y la licencia persistida → límites en memoria (Free si no hay clave).
    using (var scope = app.Services.CreateScope())
    {
        var sites = scope.ServiceProvider.GetRequiredService<ISiteRepository>();
        var company = (await sites.GetCompaniesAsync()).FirstOrDefault();
        if (company is not null)
        {
            var site = (await sites.GetByCompanyAsync(company.Id)).FirstOrDefault();
            if (site is not null)
                app.Services.GetRequiredService<AmbientTenantProvider>().Set(company.Id, site.Id);

            app.Services.GetRequiredService<CurrentLicense>().State =
                app.Services.GetRequiredService<ILicenseService>()
                    .Resolve(company.LicenseKey, DateOnly.FromDateTime(DateTime.Now));
        }
    }

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
