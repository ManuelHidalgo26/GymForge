using System.Text.Json;
using GymForge.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Preferencias de ESTA PC en un JSON junto a la base local (<c>%LOCALAPPDATA%\GymForge\settings.json</c>).
/// Se cargan al construir; si el archivo falta o está corrupto arranca con los valores por defecto.
/// Best-effort: nunca lanza. Si el disco falla, los valores siguen vigentes en memoria durante la sesión.
/// </summary>
public sealed partial class LocalSettingsService : ILocalSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<LocalSettingsService> _log;
    private readonly string _path;
    private readonly Lock _saveGate = new();

    public LocalSettingsService(ILogger<LocalSettingsService> log, string? path = null)
    {
        _log = log;
        _path = path ?? DefaultPath;
        Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GymForge", "settings.json");

    public string? OffsiteBackupDir { get; set; }

    public DateTime? LastOffsiteBackupUtc { get; set; }

    public string? UpdateFeedUrl { get; set; }

    /// <summary>Escribe a un temporal y lo mueve encima: un corte a mitad de camino no deja el JSON a medias.</summary>
    public void Save()
    {
        lock (_saveGate)
        {
            var temp = _path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var stored = new StoredSettings(OffsiteBackupDir, LastOffsiteBackupUtc, UpdateFeedUrl);
                File.WriteAllText(temp, JsonSerializer.Serialize(stored, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogWriteFailed(ex, _path);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;

            var stored = JsonSerializer.Deserialize<StoredSettings>(File.ReadAllText(_path));
            OffsiteBackupDir = stored?.OffsiteBackupDir;
            LastOffsiteBackupUtc = stored?.LastOffsiteBackupUtc;
            UpdateFeedUrl = stored?.UpdateFeedUrl;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogReadFailed(ex, _path);
        }
    }

    /// <summary>Forma persistida: separada del servicio para que el JSON no dependa de sus dependencias.</summary>
    private sealed record StoredSettings(
        string? OffsiteBackupDir, DateTime? LastOffsiteBackupUtc, string? UpdateFeedUrl = null);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Settings locales: no se pudieron guardar en {Path}")]
    private partial void LogWriteFailed(Exception ex, string path);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Settings locales: no se pudieron leer de {Path}; se usan los valores por defecto")]
    private partial void LogReadFailed(Exception ex, string path);
}
