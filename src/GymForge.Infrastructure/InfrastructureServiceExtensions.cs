using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Seed;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<GymForgeDbContext>(opts =>
            opts.UseSqlite(connectionString,
                sql => sql.MigrationsAssembly(typeof(GymForgeDbContext).Assembly.FullName)));

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IChargeRepository, ChargeRepository>();
        services.AddScoped<IAccessLogRepository, AccessLogRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IEventBus, InProcessEventBus>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    /// <summary>Runs EF migrations and seeds initial data on first launch.</summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();

        await db.Database.MigrateAsync();
        await seeder.SeedAsync();
    }
}
