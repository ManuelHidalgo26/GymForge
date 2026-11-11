using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Maintenance;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Persistence.Interceptors;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Seed;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        // El interceptor de auditoría depende de ICurrentUser / ICurrentTenant (los registra el host antes
        // de AddInfrastructure) y se resuelve por scope: de ahí el overload (sp, opts) de AddDbContext.
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<GymForgeDbContext>((sp, opts) =>
            opts.UseSqlite(connectionString,
                    sql => sql.MigrationsAssembly(typeof(GymForgeDbContext).Assembly.GetName().Name))
                .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));

        // Preferencias de ESTA PC (destino del backup externo, etc.): archivo local, no viaja con la base.
        services.AddSingleton<ILocalSettings, LocalSettingsService>();

        // Huella de ESTA PC para atar la licencia (la consume LicenseService; sin ella no se chequea el atado).
        services.AddSingleton<IMachineFingerprint>(sp => new WindowsMachineFingerprint(
            sp.GetRequiredService<ILogger<WindowsMachineFingerprint>>()));

        // Frase de acceso del respaldo cifrado, protegida con DPAPI en esta PC (ADR-012). GymForge es solo Windows.
        services.AddSingleton<IBackupKeyStore>(sp => OperatingSystem.IsWindows()
            ? new DpapiBackupKeyStore(sp.GetRequiredService<ILogger<DpapiBackupKeyStore>>())
            : throw new PlatformNotSupportedException("El cifrado de respaldos requiere Windows (DPAPI)."));

        // Resguardo y recuperación de la base local (singleton: tiene la ruta de la DB).
        services.AddSingleton(sp => new DatabaseBackupService(
            connectionString,
            sp.GetRequiredService<ILogger<DatabaseBackupService>>(),
            sp.GetRequiredService<ILocalSettings>(),
            sp.GetRequiredService<IBackupKeyStore>()));
        services.AddSingleton<IDatabaseBackup>(sp => sp.GetRequiredService<DatabaseBackupService>());

        // Migración de datos entre PCs (exportar/importar el gimnasio completo).
        services.AddSingleton<IDataTransfer>(sp => new DataTransferService(
            connectionString,
            sp.GetRequiredService<IDatabaseBackup>(),
            sp.GetRequiredService<IBackupKeyStore>(),
            sp.GetRequiredService<ILogger<DataTransferService>>()));

        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IChargeRepository, ChargeRepository>();
        services.AddScoped<IMemberCreditRepository, MemberCreditRepository>();
        services.AddScoped<IAccessLogRepository, AccessLogRepository>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IShiftRepository, ShiftRepository>();
        services.AddScoped<ISiteRepository, SiteRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IMembershipTypeRepository, MembershipTypeRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IExerciseRepository, ExerciseRepository>();
        services.AddScoped<IRoutineRepository, RoutineRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPinHasher, Pbkdf2PinHasher>();
        services.AddSingleton<IReceiptPdfWriter, ReceiptPdfGenerator>();

        // Exportador genérico de reportes (PDF con QuestPDF, Excel con ClosedXML). Los generadores no
        // guardan estado, como ReceiptPdfGenerator.
        services.AddSingleton<ReportPdfGenerator>();
        services.AddSingleton<ReportXlsxGenerator>();
        services.AddSingleton<IReportExporter, ReportExporter>();
        services.AddScoped<IEventBus, InProcessEventBus>();

        // Notificaciones + cobro automático (dunning). Sender por defecto = log; el
        // proveedor real (Twilio/Wassenger) se registra en su lugar. Off hasta configurar.
        services.AddSingleton<INotificationSender, LogNotificationSender>();
        services.AddSingleton<GymForge.Application.UseCases.Dunning.DunningConfig>();
        services.AddScoped<GymForge.Application.UseCases.Dunning.DunningService>();

        // Mantenimiento diario con catch-up: vence membresías, marca mora y dispara el dunning.
        // El checkpoint es singleton: guarda en archivo el último día local procesado.
        services.AddScoped<DailyMaintenanceService>();
        services.AddScoped<IDailyMaintenanceRunner, DailyMaintenanceRunner>();
        services.AddSingleton<IMaintenanceCheckpoint, FileMaintenanceCheckpoint>();

        services.AddScoped<DatabaseSeeder>();

        return services;
    }

    /// <summary>Runs EF migrations and seeds initial data on first launch.</summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
        var backup = scope.ServiceProvider.GetRequiredService<DatabaseBackupService>();

        // Si la base está corrupta, archivarla y restaurar el último backup ANTES de abrirla.
        backup.EnsureHealthy();

        await BaselineLegacyDatabaseAsync(db);
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GymForgeDbContext>>();
        await MemberDocumentMigrationGuard.EnsureNoDuplicatesAsync(db, logger);
        await db.Database.MigrateAsync();
        await EnableWriteAheadLoggingAsync(db);
        await MemberStatusReconciliation.ReconcileAsync(db, backup, logger);
        await seeder.SeedAsync();

        // Datos de demo para probar a mano (opt-in por env var).
        if (Environment.GetEnvironmentVariable("GYMFORGE_SEED_DEMO") == "1")
            await DemoDataSeeder.SeedAsync(scope.ServiceProvider);

        // Snapshot diario (no duplica si ya hay uno de hoy), con la base ya sana y migrada.
        backup.BackupIfDue();
    }

    /// <summary>
    /// SQLite local en WAL (CLAUDE.md): lectores y escritor no se bloquean entre sí (la UI y el mantenimiento diario
    /// escriben a la vez). Las migraciones no lo dejan activado, así que se fuerza en cada arranque: el modo se
    /// guarda en el archivo, de modo que es idempotente y también corrige bases viejas o importadas.
    /// </summary>
    private static Task EnableWriteAheadLoggingAsync(GymForgeDbContext db) =>
        db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL");

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
