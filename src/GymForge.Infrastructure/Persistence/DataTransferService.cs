using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using GymForge.Application.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Persistence;

/// <summary>
/// Migración de los datos del gimnasio entre PCs. Empaqueta en un zip la base SQLite
/// (copia consistente, con la app en uso) más los archivos que la base referencia por
/// ruta: el logo y las fotos. Al importar, además de reemplazar, reescribe esas rutas
/// para que apunten a la carpeta de la PC nueva — si no, las imágenes quedarían rotas
/// porque el perfil de usuario de Windows es otro.
///
/// No se exportan: recibos PDF (se regeneran desde la base), backups, logs ni la
/// carpeta vendor (claves privadas del vendedor, que no son datos del gimnasio).
/// </summary>
public class DataTransferService : IDataTransfer
{
    /// <summary>Versión del formato del paquete. Se sube si cambia la estructura del zip.</summary>
    public const int FormatVersion = 1;

    private const string ManifestEntry = "manifest.json";
    private const string DbEntry = "gymforge.db";
    private const string FilesRoot = "archivos";

    /// <summary>Carpetas de datos del usuario que viajan con la base.</summary>
    private static readonly string[] UserFolders = ["brand", "fotos"];

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _dataDir;
    private readonly IDatabaseBackup _backup;
    private readonly ILogger<DataTransferService> _logger;

    public DataTransferService(
        string connectionString, IDatabaseBackup backup, ILogger<DataTransferService> logger)
    {
        _connectionString = connectionString;
        _dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
        _dataDir = Path.GetDirectoryName(_dbPath)!;
        _backup = backup;
        _logger = logger;
    }

    public string SuggestedFileName(string gymName)
    {
        var limpio = new string([.. gymName.Where(char.IsLetterOrDigit)]);
        if (limpio.Length == 0) limpio = "GymForge";
        return $"GymForge-{limpio}-{DateTime.Now:yyyy-MM-dd}.zip";
    }

    // ── exportar ────────────────────────────────────────────────────────────

    public async Task<DataPackageInfo> ExportAsync(string destinationZipPath, CancellationToken ct = default)
    {
        var temp = NewTempDir();
        try
        {
            var dbCopy = Path.Combine(temp, DbEntry);
            BackupDatabaseTo(dbCopy);

            var fotos = 0;
            foreach (var carpeta in UserFolders)
            {
                var origen = Path.Combine(_dataDir, carpeta);
                if (!Directory.Exists(origen)) continue;
                var destino = Path.Combine(temp, FilesRoot, carpeta);
                fotos += CopyDirectory(origen, destino);
            }

            var info = ReadInfo(dbCopy, fotos);
            await File.WriteAllTextAsync(
                Path.Combine(temp, ManifestEntry), JsonSerializer.Serialize(info, JsonOpts), ct);

            var dir = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (File.Exists(destinationZipPath)) File.Delete(destinationZipPath);
            ZipFile.CreateFromDirectory(temp, destinationZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);

            _logger.LogInformation("Datos exportados a {File}", destinationZipPath);
            return info;
        }
        finally
        {
            TryDeleteDir(temp);
        }
    }

    /// <summary>
    /// Cadena de conexión para un archivo temporal. Sin pooling: si no, el handle
    /// sobrevive al Dispose y después no se puede comprimir ni borrar el archivo.
    /// </summary>
    private static string TempConnection(string dbPath, bool readOnly = false) =>
        $"Data Source={dbPath};Pooling=False" + (readOnly ? ";Mode=ReadOnly" : "");

    /// <summary>Copia consistente con el backup API: no requiere cerrar la app.</summary>
    private void BackupDatabaseTo(string destination)
    {
        using var source = new SqliteConnection(_connectionString);
        using var target = new SqliteConnection(TempConnection(destination));
        source.Open();
        target.Open();
        source.BackupDatabase(target);
    }

    private DataPackageInfo ReadInfo(string dbPath, int fotos)
    {
        using var conn = new SqliteConnection(TempConnection(dbPath, readOnly: true));
        conn.Open();

        var gym = Scalar(conn, "SELECT LegalName FROM Companies LIMIT 1") ?? "GymForge";
        var logo = Scalar(conn, "SELECT LogoUrl FROM Companies LIMIT 1");

        return new DataPackageInfo(
            FormatVersion,
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
            gym,
            DateTime.Now,
            Count(conn, "Members"),
            Count(conn, "Payments"),
            !string.IsNullOrWhiteSpace(logo),
            fotos);
    }

    // ── inspeccionar ────────────────────────────────────────────────────────

    public async Task<DataPackageInfo> InspectAsync(string zipPath, CancellationToken ct = default)
    {
        if (!File.Exists(zipPath))
            throw new DataPackageException("No se encontró el archivo.");

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(zipPath);
        }
        catch (InvalidDataException ex)
        {
            throw new DataPackageException("El archivo está dañado o no es un .zip válido.", ex);
        }

        using (archive)
        {
            var manifest = archive.GetEntry(ManifestEntry)
                ?? throw new DataPackageException("Este archivo no es una exportación de GymForge.");

            DataPackageInfo? info;
            try
            {
                using var reader = new StreamReader(manifest.Open());
                info = JsonSerializer.Deserialize<DataPackageInfo>(await reader.ReadToEndAsync(ct));
            }
            catch (JsonException ex)
            {
                throw new DataPackageException("Este archivo no es una exportación de GymForge.", ex);
            }

            if (info is null)
                throw new DataPackageException("Este archivo no es una exportación de GymForge.");

            if (info.FormatVersion > FormatVersion)
                throw new DataPackageException(
                    $"El paquete fue exportado con una versión más nueva de GymForge ({info.AppVersion}). " +
                    "Actualizá la app antes de importarlo.");

            var db = archive.GetEntry(DbEntry)
                ?? throw new DataPackageException("El paquete no contiene la base de datos.");

            // Se valida la integridad ANTES de tocar nada: si viene dañada, no se importa.
            var temp = NewTempDir();
            try
            {
                var copia = Path.Combine(temp, DbEntry);
                db.ExtractToFile(copia, overwrite: true);
                if (!IsHealthy(copia))
                    throw new DataPackageException("La base de datos del paquete está dañada.");
            }
            finally
            {
                TryDeleteDir(temp);
            }

            return info;
        }
    }

    private bool IsHealthy(string dbPath)
    {
        try
        {
            using var conn = new SqliteConnection(TempConnection(dbPath, readOnly: true));
            conn.Open();
            return Scalar(conn, "PRAGMA integrity_check") == "ok";
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "integrity_check falló sobre el paquete importado.");
            return false;
        }
    }

    // ── importar ────────────────────────────────────────────────────────────

    public async Task ImportAsync(string zipPath, CancellationToken ct = default)
    {
        // Valida primero: si el paquete no sirve, los datos actuales quedan intactos.
        var info = await InspectAsync(zipPath, ct);

        _backup.BackupNow("pre-import");
        SqliteConnection.ClearAllPools();

        var temp = NewTempDir();
        try
        {
            ZipFile.ExtractToDirectory(zipPath, temp);

            // Los archivos primero: si algo falla acá, la base todavía es la vieja.
            foreach (var carpeta in UserFolders)
            {
                var origen = Path.Combine(temp, FilesRoot, carpeta);
                if (Directory.Exists(origen)) CopyDirectory(origen, Path.Combine(_dataDir, carpeta));
            }

            ReplaceDatabase(Path.Combine(temp, DbEntry));
            RepairFilePaths();

            _logger.LogWarning(
                "Datos reemplazados por el paquete de {Gym} ({Members} socios, exportado {Date:d}).",
                info.GymName, info.Members, info.ExportedAt);
        }
        finally
        {
            TryDeleteDir(temp);
        }
    }

    private void ReplaceDatabase(string nueva)
    {
        // El DbContext puede tener el archivo tomado un instante más; se reintenta.
        for (var intento = 1; ; intento++)
        {
            try
            {
                File.Copy(nueva, _dbPath, overwrite: true);
                foreach (var ext in new[] { "-wal", "-shm" })
                {
                    var side = _dbPath + ext;
                    if (File.Exists(side)) File.Delete(side);
                }
                return;
            }
            catch (IOException) when (intento < 5)
            {
                SqliteConnection.ClearAllPools();
                Thread.Sleep(200 * intento);
            }
        }
    }

    /// <summary>
    /// Reapunta las rutas de archivos a la carpeta de datos de ESTA PC. La base trae
    /// rutas absolutas del equipo de origen (otro perfil de Windows), que acá no existen.
    /// </summary>
    private void RepairFilePaths()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var tx = conn.BeginTransaction();

        Reapuntar(conn, "Companies", "LogoUrl", "brand");
        Reapuntar(conn, "Members", "PhotoUrl", "fotos");
        foreach (var columna in new[] { "PhotoFront", "PhotoSide", "PhotoBack" })
            Reapuntar(conn, "BodyMeasurements", columna, "fotos");

        tx.Commit();
    }

    private void Reapuntar(SqliteConnection conn, string tabla, string columna, string carpeta)
    {
        var cambios = new List<(string Id, string Ruta)>();

        try
        {
            using var read = conn.CreateCommand();
            read.CommandText =
                $"SELECT Id, {columna} FROM {tabla} WHERE {columna} IS NOT NULL AND {columna} <> ''";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var actual = reader.GetString(1);
                var nueva = Path.Combine(_dataDir, carpeta, Path.GetFileName(actual));
                if (!string.Equals(actual, nueva, StringComparison.OrdinalIgnoreCase))
                    cambios.Add((reader.GetString(0), nueva));
            }
        }
        catch (SqliteException ex)
        {
            // Tabla o columna inexistente (paquete de un esquema anterior): no es fatal.
            _logger.LogWarning(ex, "No se pudieron reapuntar las rutas de {Tabla}.{Columna}.", tabla, columna);
            return;
        }

        foreach (var (id, ruta) in cambios)
        {
            using var update = conn.CreateCommand();
            update.CommandText = $"UPDATE {tabla} SET {columna} = $ruta WHERE Id = $id";
            update.Parameters.AddWithValue("$ruta", ruta);
            update.Parameters.AddWithValue("$id", id);
            update.ExecuteNonQuery();
        }
    }

    // ── utilidades ──────────────────────────────────────────────────────────

    private static string? Scalar(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar() as string;
    }

    private static int Count(SqliteConnection conn, string tabla)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {tabla}";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return 0;   // esquema anterior sin esa tabla
        }
    }

    /// <summary>Copia recursiva. Devuelve la cantidad de archivos copiados.</summary>
    private static int CopyDirectory(string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        var copiados = 0;

        foreach (var archivo in Directory.GetFiles(origen))
        {
            File.Copy(archivo, Path.Combine(destino, Path.GetFileName(archivo)), overwrite: true);
            copiados++;
        }

        foreach (var sub in Directory.GetDirectories(origen))
            copiados += CopyDirectory(sub, Path.Combine(destino, Path.GetFileName(sub)));

        return copiados;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_transfer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
    }
}
