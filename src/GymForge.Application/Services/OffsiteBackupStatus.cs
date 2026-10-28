using System.Globalization;

namespace GymForge.Application.Services;

/// <summary>
/// Estado del resguardo externo listo para Configuración: el texto a mostrar y si hay que llamar la
/// atención (nunca se copió, la copia está vieja o la carpeta de destino no está disponible hoy).
/// </summary>
public sealed record OffsiteBackupStatus(string Display, bool NeedsAttention)
{
    private const int MaxAgeDays = 7;

    /// <param name="dir">Carpeta de destino configurada; vacía = sin copia externa.</param>
    /// <param name="lastCopyLocal">Hora local de la última copia exitosa; null = nunca.</param>
    /// <param name="destinationAvailable">Si la carpeta de destino existe ahora (pendrive enchufado, red arriba).</param>
    /// <param name="nowLocal">Hora local actual.</param>
    public static OffsiteBackupStatus Evaluate(
        string? dir, DateTime? lastCopyLocal, bool destinationAvailable, DateTime nowLocal)
    {
        if (string.IsNullOrWhiteSpace(dir))
            return new("Sin copia externa: los resguardos quedan solo en esta PC.", false);

        if (lastCopyLocal is not { } last)
            return destinationAvailable
                ? new("Todavía no se hizo ningún resguardo externo.", true)
                : new("Todavía no se hizo ningún resguardo externo y la carpeta de destino no está disponible.", true);

        var text = $"Último resguardo externo: {last.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)}";
        if (!destinationAvailable)
            return new($"{text} — la carpeta de destino no está disponible (¿pendrive desconectado?).", true);

        return nowLocal - last > TimeSpan.FromDays(MaxAgeDays)
            ? new($"{text} — hace más de {MaxAgeDays} días.", true)
            : new(text, false);
    }
}
