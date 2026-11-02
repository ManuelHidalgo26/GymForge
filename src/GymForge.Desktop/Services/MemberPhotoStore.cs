using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>Guarda en disco la foto de un socio. Application no toca archivos: recibe la ruta que devuelve esto.</summary>
public interface IMemberPhotoStore
{
    /// <summary>Escribe la foto del socio (reemplaza la anterior) y devuelve la ruta absoluta del archivo.</summary>
    /// <exception cref="InvalidDataException">La imagen no es un JPG o PNG, o es demasiado grande.</exception>
    /// <exception cref="IOException">No se pudo escribir el archivo.</exception>
    string Save(Guid memberId, byte[] image);
}

/// <summary>
/// Las fotos viven en <c>%LOCALAPPDATA%\GymForge\fotos\{socio}.jpg|png</c> con ruta absoluta, igual que el logo:
/// esa carpeta es la que <c>DataTransferService</c> copia y reapunta al migrar los datos a otra PC.
/// </summary>
public sealed class MemberPhotoStore : IMemberPhotoStore
{
    private readonly string _directory;

    public MemberPhotoStore(string directory) => _directory = directory;

    public static string DefaultDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymForge", "fotos");

    public string Save(Guid memberId, byte[] image)
    {
        var extension = PhotoImage.RequireExtension(image);
        Directory.CreateDirectory(_directory);

        var path = Path.Combine(_directory, memberId + extension);
        File.WriteAllBytes(path, image);
        DeleteOtherFormats(memberId, keep: path);
        return path;
    }

    // Una foto nueva en otro formato (era .png y ahora .jpg) no deja la anterior huérfana en la carpeta.
    private void DeleteOtherFormats(Guid memberId, string keep)
    {
        foreach (var old in Directory.GetFiles(_directory, memberId + ".*"))
        {
            if (string.Equals(old, keep, StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                File.Delete(old);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Warning(ex, "No se pudo borrar la foto anterior {Path}", old);
            }
        }
    }
}
