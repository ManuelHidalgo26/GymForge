namespace GymForge.Application.Interfaces;

/// <summary>
/// Preferencias locales de ESTA PC (destino del backup externo, etc.). NO viven en la base ni viajan
/// con el export de datos: son específicas de la máquina (un pendrive <c>F:\</c> puede no existir en otra).
/// Best-effort: leer y guardar nunca lanza.
/// </summary>
public interface ILocalSettings
{
    /// <summary>Carpeta (pendrive, red, carpeta sincronizada) donde se copia el último backup. Null = sin destino externo.</summary>
    string? OffsiteBackupDir { get; set; }

    /// <summary>Cuándo se completó con éxito la última copia al destino externo (UTC). Null = nunca.</summary>
    DateTime? LastOffsiteBackupUtc { get; set; }

    /// <summary>
    /// Origen de las actualizaciones automáticas: URL del repositorio de GitHub con los releases, de un
    /// servidor web o carpeta de red con los archivos de Velopack. Null o vacío = auto-actualización inactiva.
    /// </summary>
    string? UpdateFeedUrl { get; set; }

    /// <summary>Persiste los valores actuales. No lanza: si el disco falla queda el valor en memoria.</summary>
    void Save();
}
