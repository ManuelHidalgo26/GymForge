using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Seed;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
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
                sql => sql.MigrationsAssembly(typeof(GymForgeDbContext).Assembly.GetName().Name)));

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IChargeRepository, ChargeRepository>();
        services.AddScoped<IAccessLogRepository, AccessLogRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IMembershipTypeRepository, MembershipTypeRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IExerciseRepository, ExerciseRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<IReceiptPdfWriter, ReceiptPdfGenerator>();
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

        await BaselineLegacyDatabaseAsync(db);
        await db.Database.MigrateAsync();
        await seeder.SeedAsync();
    }

    // Versión informativa que se registra en __EFMigrationsHistory al hacer baseline.
    private const string MigrationProductVersion = "9.0.4";

    /// <summary>
    /// Si la base fue creada con EnsureCreated (tiene tablas pero no historial de
    /// migraciones), registra las migraciones como ya aplicadas en vez de que
    /// MigrateAsync intente recrear el schema. Cubre el upgrade EnsureCreated→Migrate.
    /// </summary>
    private static async Task BaselineLegacyDatabaseAsync(GymForgeDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();

        // DB nueva o vacía: MigrateAsync la crea normalmente.
        if (!await creator.ExistsAsync() || !await creator.HasTablesAsync())
            return;

        // Ya gestionada por migraciones: nada que hacer.
        if ((await db.Database.GetAppliedMigrationsAsync()).Any())
            return;

        // Base "legacy": crear la tabla de historial e insertar cada migración existente
        // como aplicada, sin re-ejecutar su SQL (el schema ya está completo).
        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript());

        var migrationsAssembly = db.GetService<IMigrationsAssembly>();
        foreach (var migrationId in migrationsAssembly.Migrations.Keys)
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, MigrationProductVersion)));
    }
}
