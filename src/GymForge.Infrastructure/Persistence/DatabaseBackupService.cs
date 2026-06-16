using GymForge.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Resguardo y recuperación de la base SQLite local. Hace copias consistentes con el
/// backup API de SQLite (funciona con la base en uso), rota las viejas y, al arrancar,
/// si la base está corrupta la archiva y restaura el último backup. Best-effort: el
/// backup nunca tira (no debe romper una operación de negocio).
/// </summary>
public class DatabaseBackupService : IDatabaseBackup
{
    public enum StartupOutcome { Healthy, RestoredFromBackup, RecreatedEmpty }

    private const int MaxBackups = 10;

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(string connectionString, ILogger<DatabaseBackupService> logger)
    {
        _connectionString = connectionString;
        _dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        _backupDir = Path.Combine(Path.GetDirectoryName(_dbPath)!, "backups");
        _logger = logger;
    }

    /// <summary>
    /// Verifica la integridad de la base antes de usarla. Si está corrupta, la archiva
    /// como gymforge.corrupt-* y restaura el backup más reciente (o deja que se cree una
    /// nueva si no hay backups). Llamar ANTES de abrir el DbContext.
    /// </summary>
    public StartupOutcome EnsureHealthy()
    {
        if (!File.Exists(_dbPath)) return StartupOutcome.Healthy;   // base nueva: Migrate la crea
        if (IsHealthy()) return StartupOutcome.Healthy;

        _logger.LogError("Base de datos corrupta detectada ({Db}). Intentando recuperar.", _dbPath);
        SqliteConnection.ClearAllPools();   // soltar handles antes de tocar el archivo
        ArchiveCorrupt();

        var latest = LatestBackup();
        if (latest is not null)
        {
            File.Copy(latest, _dbPath, overwrite: true);
            _logger.LogWarning("Base restaurada desde el backup {Backup}.", Path.GetFileName(latest));
            return StartupOutcome.RestoredFromBackup;
        }

        _logger.LogWarning("No hay backups disponibles: se regenerará una base nueva.");
        return StartupOutcome.RecreatedEmpty;
    }

    /// <summary>Crea un backup solo si todavía no hay uno de hoy (snapshot diario al arrancar).</summary>
    public void BackupIfDue()
    {
        var latest = LatestBackup();
        if (latest is not null && File.GetLastWriteTime(latest).Date == DateTime.Today) return;
        BackupNow("diario");
    }

    public void BackupNow(string reason = "manual")
    {
        try
        {
            if (!File.Exists(_dbPath)) return;
            Directory.CreateDirectory(_backupDir);
            var dest = Path.Combine(_backupDir, $"gymforge-{DateTime.Now:yyyyMMdd-HHmmss}.db");

            using (var source = new SqliteConnection(_connectionString))
            using (var destination = new SqliteConnection($"Data Source={dest}"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);   // copia consistente, base en uso OK
            }

            _logger.LogInformation("Backup creado ({Reason}): {File}", reason, Path.GetFileName(dest));
            Rotate();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo crear el backup de la base.");
        }
    }

    private bool IsHealthy()
    {
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA integrity_check";
            return cmd.ExecuteScalar() as string == "ok";
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "integrity_check falló: la base parece corrupta.");
            return false;
        }
    }

    private void ArchiveCorrupt()
    {
        try
        {
            var target = Path.Combine(
                Path.GetDirectoryName(_dbPath)!, $"gymforge.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.db");
            File.Move(_dbPath, target, overwrite: true);
            foreach (var ext in new[] { "-wal", "-shm" })
                if (File.Exists(_dbPath + ext)) File.Delete(_dbPath + ext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo archivar la base corrupta.");
        }
    }

    private void Rotate()
    {
        foreach (var old in Directory.GetFiles(_backupDir, "gymforge-*.db")
                     .OrderByDescending(File.GetLastWriteTime)
                     .Skip(MaxBackups))
        {
            try { File.Delete(old); } catch { /* best-effort */ }
        }
    }

    private string? LatestBackup() =>
        Directory.Exists(_backupDir)
            ? Directory.GetFiles(_backupDir, "gymforge-*.db")
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault()
            : null;
}
