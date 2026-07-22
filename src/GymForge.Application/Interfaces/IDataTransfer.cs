namespace GymForge.Application.Interfaces;

/// <summary>
/// Contenido de un paquete de exportación. Se guarda como manifest.json dentro del zip
/// y se muestra al usuario antes de importar, para que confirme que es el archivo correcto.
/// </summary>
public record DataPackageInfo(
    int FormatVersion,
    string AppVersion,
    string GymName,
    DateTime ExportedAt,
    int Members,
    int Payments,
    bool HasLogo,
    int Photos);

/// <summary>Paquete inválido, corrupto o de una versión incompatible. El mensaje se muestra tal cual.</summary>
public class DataPackageException : Exception
{
    public DataPackageException(string message) : base(message) { }
    public DataPackageException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Migración de los datos del gimnasio entre PCs: exporta la base y los archivos
/// (logo, fotos) a un zip, y lo restaura en otra instalación.
/// </summary>
public interface IDataTransfer
{
    /// <summary>Nombre sugerido para el archivo, ej. GymForge-IronTemple-2026-07-22.zip</summary>
    string SuggestedFileName(string gymName);

    /// <summary>Arma el paquete en <paramref name="destinationZipPath"/>. No interrumpe el uso de la app.</summary>
    Task<DataPackageInfo> ExportAsync(string destinationZipPath, CancellationToken ct = default);

    /// <summary>Valida el paquete y devuelve su contenido, sin modificar nada.</summary>
    Task<DataPackageInfo> InspectAsync(string zipPath, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza los datos locales por los del paquete, respaldando antes lo que había.
    /// La app debe reiniciarse después: el DbContext y las pantallas quedaron con datos viejos.
    /// </summary>
    Task ImportAsync(string zipPath, CancellationToken ct = default);
}
