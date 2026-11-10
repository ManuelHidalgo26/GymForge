using GymForge.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Resguardo y recuperación de la base SQLite local. Hace copias consistentes con el
/// backup API de SQLite (funciona con la base en uso), rota las viejas y, al arrancar,
/// si la base está corrupta la archiva y restaura el último backup. Si hay un destino
/// externo configurado (pendrive, red, carpeta sincronizada) copia ahí cada backup nuevo.
/// Best-effort: el backup nunca tira (no debe romper una operación de negocio).
/// </summary>
public class DatabaseBackupService : IDatabaseBackup
{
    public enum StartupOutcome { Healthy, RestoredFromBackup, RecreatedEmpty }

    private const int MaxBackups = 10;
    private const string BackupFilePattern = "gymforge-*.db";

    // Subcarpeta del destino externo: no ensucia la raíz del pendrive y permite compartir la unidad.
    private const string OffsiteFolderName = "GymForge-backups";

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly ILogger<DatabaseBackupService> _logger;
    private readonly ILocalSettings? _settings;

    /// <param name="settings">Preferencias de esta PC. Null = sin destino externo (solo backup local).</param>
    public DatabaseBackupService(
        string connectionString, ILogger<DatabaseBackupService> logger, ILocalSettings? settings = null)
    {
        _connectionString = connectionString;
        _dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        _backupDir = Path.Combine(Path.GetDirectoryName(_dbPath)!, "backups");
        _logger = logger;
        _settings = settings;
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

    /// <summary>Última copia externa disparada por <see cref="BackupNow"/>. Solo la esperan los tests.</summary>
    internal Task PendingOffsiteCopy { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Backup local síncrono (true = quedó hecho). La copia externa sigue en segundo plano: una carpeta de red
    /// caída puede colgar decenas de segundos y esto corre en el arranque y en el hilo de UI (cierre de caja).
    /// </summary>
    public bool BackupNow(string reason = "manual")
    {
        var backup = CreateLocalBackup(reason);
        if (backup is null) return false;

        PendingOffsiteCopy = Task.Run(() => CopyOffsiteInBackground(backup));
        return true;
    }

    public OffsiteBackupOutcome BackupOffsiteNow()
    {
        var backup = CreateLocalBackup("externo manual");
        return backup is null ? OffsiteBackupOutcome.LocalBackupFailed : CopyOffsite(backup);
    }

    /// <summary>Fire-and-forget: nada se propaga (ni como UnobservedTaskException); el resultado no se informa.</summary>
    private void CopyOffsiteInBackground(string backupPath)
    {
        try
        {
            CopyOffsite(backupPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la copia del backup al destino externo en segundo plano.");
        }
    }

    /// <summary>Crea el backup local y rota los viejos. Devuelve su ruta, o null si no se pudo crear.</summary>
    private string? CreateLocalBackup(string reason)
    {
        try
        {
            if (!File.Exists(_dbPath)) return null;
            Directory.CreateDirectory(_backupDir);
            var dest = Path.Combine(_backupDir, $"gymforge-{DateTime.Now:yyyyMMdd-HHmmss}-{Slug(reason)}.db");

            using (var source = new SqliteConnection(_connectionString))
            using (var destination = new SqliteConnection($"Data Source={dest}"))
            {
                source.Open();
                destination.Open();
                source.BackupDatabase(destination);   // copia consistente, base en uso OK
            }

            _logger.LogInformation("Backup creado ({Reason}): {File}", reason, Path.GetFileName(dest));
            Rotate(_backupDir);
            return dest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo crear el backup de la base.");
            return null;
        }
    }

    /// <summary>
    /// El motivo en el nombre del archivo: minúsculas ASCII, el resto colapsado a un guion. Siempre sigue
    /// matcheando <see cref="BackupFilePattern"/>, así que la rotación y la búsqueda del último backup no cambian.
    /// </summary>
    private static string Slug(string reason)
    {
        var chars = reason.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "manual" : slug;
    }

    /// <summary>
    /// Copia el backup al destino externo y rota las copias de ahí. Pendrive desenchufado, red caída o sin
    /// permisos se loguean y se informan, pero nunca se propagan: el backup local ya quedó hecho.
    /// </summary>
    private OffsiteBackupOutcome CopyOffsite(string backupPath)
    {
        var dir = _settings?.OffsiteBackupDir;
        if (_settings is null || string.IsNullOrWhiteSpace(dir)) return OffsiteBackupOutcome.NotConfigured;

        // La carpeta configurada no se crea: si el pendrive no está (o su letra la tomó otro disco),
        // no hay destino, y no se vuelcan datos de socios en una unidad que no es la elegida.
        if (!Directory.Exists(dir))
        {
            _logger.LogWarning("El destino externo {Dir} no está disponible: se omite la copia del backup.", dir);
            return OffsiteBackupOutcome.Failed;
        }

        try
        {
            var offsiteDir = Path.Combine(dir, OffsiteFolderName);
            Directory.CreateDirectory(offsiteDir);
            DeleteOrphanTemps(offsiteDir);
            CopyAtomically(backupPath, Path.Combine(offsiteDir, Path.GetFileName(backupPath)));
            Rotate(offsiteDir);

            _settings.LastOffsiteBackupUtc = DateTime.UtcNow;
            _settings.Save();
            _logger.LogInformation("Backup externo copiado a {Dir}", offsiteDir);
            return OffsiteBackupOutcome.Copied;
        }
        catch (Exception ex)   // pendrive retirado a mitad de la copia, disco lleno, permisos...
        {
            _logger.LogWarning(ex, "No se pudo copiar el backup al destino externo {Dir}", dir);
            return OffsiteBackupOutcome.Failed;
        }
    }

    /// <summary>
    /// Copia a un temporal y lo renombra: si se retira el pendrive a mitad de camino no queda un
    /// backup trunco con nombre de válido (el temporal no matchea el patrón de backups).
    /// </summary>
    private static void CopyAtomically(string source, string target)
    {
        var temp = target + ".tmp";
        try
        {
            File.Copy(source, temp, overwrite: true);
            File.Move(temp, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* destino ya inaccesible: se informa el error original */ }
            throw;
        }
    }

    /// <summary>Borra los temporales propios que dejó una copia interrumpida (pendrive retirado a mitad de camino).</summary>
    private static void DeleteOrphanTemps(string dir)
    {
        foreach (var orphan in Directory.GetFiles(dir, BackupFilePattern + ".tmp"))
        {
            try { File.Delete(orphan); } catch { /* best-effort: se reintenta en la próxima copia */ }
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

    /// <summary>Deja los <see cref="MaxBackups"/> backups más recientes de <paramref name="dir"/> (solo los propios).</summary>
    private static void Rotate(string dir)
    {
        foreach (var old in Directory.GetFiles(dir, BackupFilePattern)
                     .OrderByDescending(File.GetLastWriteTime)
                     .Skip(MaxBackups))
        {
            try { File.Delete(old); } catch { /* best-effort */ }
        }
    }

    private string? LatestBackup() =>
        Directory.Exists(_backupDir)
            ? Directory.GetFiles(_backupDir, BackupFilePattern)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault()
            : null;
}
