using GymForge.Application.Interfaces;

namespace GymForge.Desktop.Services;

/// <summary>
/// Única fuente de verdad del origen de actualizaciones: qué origen está en uso y cuáles se aceptan.
/// </summary>
public static class UpdateFeed
{
    // Completar con la URL https del canal de releases al montarlo. NUNCA poner un token acá.
    public const string BuildDefaultUrl = "";

    private const string InsecureMessage =
        "El origen debe ser una URL https:// o una carpeta (por ejemplo \\\\servidor\\carpeta o C:\\releases). " +
        "Las direcciones http:// no se aceptan.";

    /// <summary>Origen efectivo: el configurado en esta PC si lo hay; si no, el de fábrica; si no, null.</summary>
    public static string? Resolve(ILocalSettings settings, string? buildDefault = BuildDefaultUrl)
    {
        var configured = settings.UpdateFeedUrl?.Trim();
        if (!string.IsNullOrEmpty(configured)) return configured;

        return string.IsNullOrWhiteSpace(buildDefault) ? null : buildDefault.Trim();
    }

    /// <summary>Vacío es válido (sin configurar). Se aceptan https, rutas UNC y carpetas locales; http no.</summary>
    public static bool IsAllowedSource(string? value, out string? error)
    {
        error = null;
        var source = value?.Trim();
        if (string.IsNullOrEmpty(source) || source.StartsWith(@"\\", StringComparison.Ordinal)) return true;

        if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && ((uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)) || uri.IsFile))
            return true;

        error = InsecureMessage;
        return false;
    }
}
