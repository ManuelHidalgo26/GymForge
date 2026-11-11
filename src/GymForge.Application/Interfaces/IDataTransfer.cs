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
/// (logo, fotos) a un paquete cifrado con la frase de acceso del respaldo, y lo restaura en otra instalación.
/// Al importar también se aceptan los .zip viejos, sin cifrar.
/// </summary>
public interface IDataTransfer
{
    /// <summary>Nombre sugerido para el archivo, ej. GymForge-IronTemple-2026-07-22.gfbak</summary>
    string SuggestedFileName(string gymName);

    /// <summary>
    /// Arma el paquete cifrado en <paramref name="destinationPath"/>. No interrumpe el uso de la app.
    /// Exige que la frase de acceso del respaldo esté fijada (<see cref="DataPackageException"/> si no).
    /// </summary>
    Task<DataPackageInfo> ExportAsync(string destinationPath, CancellationToken ct = default);

    /// <summary>True si el archivo es un paquete cifrado (hay que pedir la frase de acceso antes de abrirlo).</summary>
    bool IsEncrypted(string path);

    /// <summary>
    /// Valida el paquete y devuelve su contenido, sin modificar nada. <paramref name="passphrase"/> solo se usa
    /// si está cifrado; una frase incorrecta lanza <see cref="DataPackageException"/>.
    /// </summary>
    Task<DataPackageInfo> InspectAsync(string path, string? passphrase = null, CancellationToken ct = default);

    /// <summary>
    /// Reemplaza los datos locales por los del paquete, respaldando antes lo que había.
    /// La app debe reiniciarse después: el DbContext y las pantallas quedaron con datos viejos.
    /// </summary>
    Task ImportAsync(string path, string? passphrase = null, CancellationToken ct = default);
}
