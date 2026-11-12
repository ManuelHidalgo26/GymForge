using FluentAssertions;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

/// <summary>
/// Al actualizar la app, las migraciones pendientes se aplican sobre la base real del gimnasio: antes se deja una
/// copia con el motivo "pre-migracion". Una base al día o inexistente (primer arranque) no genera nada.
/// </summary>
public sealed class PreMigrationBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gymforge_premig_" + Guid.NewGuid().ToString("N"));

    public PreMigrationBackupTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string DbPath => Path.Combine(_root, "gymforge.db");
    private string ConnectionString => $"Data Source={DbPath}";
    private string BackupsDir => Path.Combine(_root, "backups");

    private GymForgeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<GymForgeDbContext>().UseSqlite(ConnectionString).Options,
            new AmbientTenantProvider());

    private DatabaseBackupService NewBackup() =>
        new(ConnectionString, NullLogger<DatabaseBackupService>.Instance, new InMemoryLocalSettings(),
            new InMemoryBackupKeyStore(InMemoryBackupKeyStore.TestPassphrase));

    private string[] PreMigrationBackups() =>
        Directory.Exists(BackupsDir) ? Directory.GetFiles(BackupsDir, "gymforge-*-pre-migracion.db") : [];

    [Fact]
    public async Task BaseExistenteConMigracionesPendientes_DejaBackupPreMigracionAntesDeMigrar()
    {
        await using (var seed = NewDb())
            await seed.Database.ExecuteSqlRawAsync("CREATE TABLE t(id INTEGER PRIMARY KEY)");
        await using var db = NewDb();

        await InfrastructureServiceExtensions.BackupBeforePendingMigrationsAsync(db, NewBackup());

        PreMigrationBackups().Should().ContainSingle();
    }

    [Fact]
    public async Task BaseAlDia_NoCreaBackupPreMigracion()
    {
        await using (var migrated = NewDb())
            await migrated.Database.MigrateAsync();
        await using var db = NewDb();

        await InfrastructureServiceExtensions.BackupBeforePendingMigrationsAsync(db, NewBackup());

        PreMigrationBackups().Should().BeEmpty();
    }

    [Fact]
    public async Task BaseInexistente_NoCreaArchivosNiRompeElPrimerArranque()
    {
        await using var db = NewDb();

        await InfrastructureServiceExtensions.BackupBeforePendingMigrationsAsync(db, NewBackup());

        PreMigrationBackups().Should().BeEmpty();
        Directory.Exists(BackupsDir).Should().BeFalse();
    }

    [Fact]
    public async Task VariosBackupsPreMigracion_NoDesplazanALosDiarios_YNoCrecenSinTope()
    {
        await using (var seed = NewDb())
            await seed.Database.ExecuteSqlRawAsync("CREATE TABLE t(id INTEGER PRIMARY KEY)");
        Directory.CreateDirectory(BackupsDir);
        var daily = Enumerable.Range(1, 10).Select(i => SeedBackup($"2020010{i % 10}-0000{i:00}-diario", daysAgo: 100 - i)).ToArray();
        var preMigration = Enumerable.Range(1, 5).Select(i => SeedBackup($"2020020{i}-000000-pre-migracion", daysAgo: 50 - i)).ToArray();
        await using var db = NewDb();

        await InfrastructureServiceExtensions.BackupBeforePendingMigrationsAsync(db, NewBackup());

        daily.Should().OnlyContain(path => File.Exists(path));
        PreMigrationBackups().Should().HaveCount(3)
            .And.NotContain(preMigration[0])
            .And.NotContain(preMigration[1])
            .And.NotContain(preMigration[2])
            .And.Contain(preMigration[3])      // quedan el nuevo + los 2 más recientes de los sembrados
            .And.Contain(preMigration[4]);
    }

    private string SeedBackup(string name, int daysAgo)
    {
        var path = Path.Combine(BackupsDir, $"gymforge-{name}.db");
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-daysAgo));
        return path;
    }
}
