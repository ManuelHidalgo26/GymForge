using System.Globalization;
using GymForge.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Checkpoint del mantenimiento diario en un archivo de texto (<c>yyyy-MM-dd</c>) junto a la base local.
/// Best-effort: nunca lanza. Si el disco falla, recuerda el último día en memoria para que esta sesión no
/// repita el mismo día en cada corrida del timer (reenviaría los avisos de cobro).
/// </summary>
public sealed partial class FileMaintenanceCheckpoint : IMaintenanceCheckpoint
{
    private const string DateFormat = "yyyy-MM-dd";

    private readonly ILogger<FileMaintenanceCheckpoint> _log;
    private readonly string _path;
    private DateOnly? _lastWritten;

    public FileMaintenanceCheckpoint(ILogger<FileMaintenanceCheckpoint> log, string? path = null)
    {
        _log = log;
        _path = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GymForge", "maintenance-last-run.txt");

    /// <summary>El más reciente entre lo que hay en disco y lo que esta sesión ya procesó.</summary>
    public DateOnly? ReadLastRun() => new[] { ReadFromDisk(), _lastWritten }.Max();

    public void WriteLastRun(DateOnly localDay)
    {
        _lastWritten = localDay;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, localDay.ToString(DateFormat, CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogWriteFailed(ex, _path);
        }
    }

    private DateOnly? ReadFromDisk()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            var text = File.ReadAllText(_path).Trim();
            return DateOnly.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogReadFailed(ex, _path);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Mantenimiento diario: no se pudo guardar el checkpoint en {Path}")]
    private partial void LogWriteFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Mantenimiento diario: no se pudo leer el checkpoint de {Path}; se trata como primera corrida")]
    private partial void LogReadFailed(Exception ex, string path);
}
