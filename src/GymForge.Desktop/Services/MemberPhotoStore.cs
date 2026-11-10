using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>Guarda en disco la foto de un socio. Application no toca archivos: recibe la ruta que devuelve esto.</summary>
public interface IMemberPhotoStore
{
    /// <summary>Escribe la foto del socio (reemplaza la anterior) y devuelve la ruta absoluta del archivo.</summary>
    /// <exception cref="InvalidDataException">La imagen no es un JPG o PNG, o es demasiado grande.</exception>
    /// <exception cref="IOException">No se pudo escribir el archivo.</exception>
    string Save(Guid memberId, byte[] image);

    /// <summary>
    /// Borra la foto del socio en el otro formato (.jpg o .png). Llamar recién cuando la foto nueva ya quedó
    /// persistida en el socio: hasta entonces la anterior sigue siendo la que referencia <c>PhotoUrl</c>.
    /// </summary>
    void DeleteOtherFormats(Guid memberId, string keptPath);
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
        WriteAtomically(path, image);
        return path;
    }

    // Temporal + Move: un corte a mitad de escritura no corrompe la foto anterior, y la otra extensión
    // recién se borra (DeleteOtherFormats) cuando la nueva ya está persistida en el socio.
    private static void WriteAtomically(string path, byte[] image)
    {
        var temp = path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, image);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* se informa el error original */ }
            throw;
        }
    }

    // Una foto nueva en otro formato (era .png y ahora .jpg) no deja la anterior huérfana en la carpeta.
    public void DeleteOtherFormats(Guid memberId, string keptPath)
    {
        foreach (var old in Directory.GetFiles(_directory, memberId + ".*"))
        {
            if (string.Equals(old, keptPath, StringComparison.OrdinalIgnoreCase)) continue;

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
