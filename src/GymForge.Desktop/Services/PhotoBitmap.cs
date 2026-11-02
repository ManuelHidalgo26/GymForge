using Avalonia.Media.Imaging;
using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>Decodifica la foto de un socio para mostrarla. Una foto dañada o ausente nunca debe romper una pantalla.</summary>
public static class PhotoBitmap
{
    /// <summary>
    /// Ancho al que se decodifica: la foto solo se muestra en recuadros de hasta ~180 px, así que 480 px alcanza
    /// (incluso con escala alta de pantalla). Decodificarla entera ocupaba ~48 MB por cada foto de 12 MP, y el kiosk
    /// carga una en cada ingreso: medido, 60 ingresos seguidos llevaron el proceso de 260 MB a 3 GB.
    /// </summary>
    public const int DecodeWidth = 480;

    /// <summary>La imagen lista para mostrar, o <c>null</c> si no se pudo decodificar.</summary>
    public static Bitmap? TryDecode(byte[] image)
    {
        try
        {
            using var stream = new MemoryStream(image);
            return Bitmap.DecodeToWidth(stream, DecodeWidth);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo decodificar la foto del socio");
            return null;
        }
    }

    /// <summary>La foto guardada en <paramref name="path"/>, o <c>null</c> si no hay ruta, el archivo no existe o está dañado.</summary>
    public static Bitmap? TryLoad(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        try
        {
            return TryDecode(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warning(ex, "No se pudo leer la foto {Path}", path);
            return null;
        }
    }
}
