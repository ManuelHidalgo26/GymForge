using FluentAssertions;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Resguardo y recuperación de la base: hace un backup, corrompe el archivo y verifica
/// que al arrancar (EnsureHealthy) la base se archiva y se restaura desde el backup.
/// Cubre el incidente real de "database disk image is malformed".
/// </summary>
public class DatabaseBackupServiceTests
{
    [Theory]
    [InlineData("pre-reconciliacion-estado-socio", "-pre-reconciliacion-estado-socio.db")]
    [InlineData("cierre de caja", "-cierre-de-caja.db")]
    [InlineData("a/b\\c:d*?", "-a-b-c-d.db")]
    [InlineData("???", "-manual.db")]
    public void BackupNow_PutsTheSanitizedReasonInTheFileName_AndReportsSuccess(string reason, string expectedSuffix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var connStr = $"Data Source={Path.Combine(dir, "gymforge.db")}";

        try
        {
            using (var c = new SqliteConnection(connStr))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY)";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var svc = new DatabaseBackupService(connStr, Substitute.For<ILogger<DatabaseBackupService>>());

            svc.BackupNow(reason).Should().BeTrue();

            Directory.GetFiles(Path.Combine(dir, "backups"), "gymforge-*.db").Should().ContainSingle()
                .Which.Should().EndWith(expectedSuffix);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
        }
    }

    [Fact]
    public void BackupNow_WhenTheDatabaseDoesNotExist_ReportsFailure()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var svc = new DatabaseBackupService(
            $"Data Source={Path.Combine(dir, "gymforge.db")}", Substitute.For<ILogger<DatabaseBackupService>>());

        try
        {
            svc.BackupNow("test").Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
        }
    }

    [Fact]
    public void EnsureHealthy_RestoresFromBackup_WhenDatabaseIsCorrupt()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "gymforge.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            // Base sana con un dato que debe sobrevivir a la recuperación.
            using (var c = new SqliteConnection(connStr))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t(v) VALUES('hola');";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var svc = new DatabaseBackupService(connStr, Substitute.For<ILogger<DatabaseBackupService>>());
            svc.BackupNow("test");
            Directory.GetFiles(Path.Combine(dir, "backups"), "gymforge-*.db").Should().ContainSingle();

            // Corromper el archivo de la base.
            SqliteConnection.ClearAllPools();
            File.WriteAllText(dbPath, "esto no es una base sqlite valida");

            // Arranque: detecta la corrupción y restaura el backup.
            svc.EnsureHealthy().Should().Be(DatabaseBackupService.StartupOutcome.RestoredFromBackup);

            // El dato volvió y la base corrupta quedó archivada.
            using (var c = new SqliteConnection(connStr))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "SELECT v FROM t LIMIT 1";
                (cmd.ExecuteScalar() as string).Should().Be("hola");
            }
            Directory.GetFiles(dir, "gymforge.corrupt-*.db").Should().NotBeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
        }
    }

    [Fact]
    public void EnsureHealthy_OnHealthyDatabase_DoesNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_backup_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var dbPath = Path.Combine(dir, "gymforge.db");
        var connStr = $"Data Source={dbPath}";

        try
        {
            using (var c = new SqliteConnection(connStr))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY)";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var svc = new DatabaseBackupService(connStr, Substitute.For<ILogger<DatabaseBackupService>>());
            svc.EnsureHealthy().Should().Be(DatabaseBackupService.StartupOutcome.Healthy);
            Directory.GetFiles(dir, "gymforge.corrupt-*.db").Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
        }
    }
}
