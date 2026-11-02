namespace GymForge.Desktop.Services;

/// <summary>
/// Reglas de la imagen de la foto del socio: JPG o PNG de hasta 10 MB. El formato se reconoce por el contenido
/// (los primeros bytes) y no por el nombre del archivo, así que una extensión engañosa no pasa.
/// </summary>
public static class PhotoImage
{
    public const int MaxBytes = 10 * 1024 * 1024;

    private static ReadOnlySpan<byte> JpegSignature => [0xFF, 0xD8, 0xFF];
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Lee la imagen de <paramref name="stream"/> y corta en cuanto pasa de <see cref="MaxBytes"/> (a lo sumo un bloque
    /// de más): un archivo gigante (un video copiado en el Explorador y "Pegar imagen") no cuelga la app ni agota la
    /// memoria, y <see cref="RequireExtension"/> lo rechaza después por tamaño.
    /// </summary>
    public static async Task<byte[]> ReadCappedAsync(Stream stream, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while (buffer.Length <= MaxBytes && (read = await stream.ReadAsync(chunk, ct)) > 0)
            buffer.Write(chunk, 0, read);

        return buffer.ToArray();
    }

    /// <summary>La extensión que corresponde a la imagen (<c>.jpg</c> o <c>.png</c>).</summary>
    /// <exception cref="InvalidDataException">No es un JPG o PNG, o pesa más de <see cref="MaxBytes"/>.</exception>
    public static string RequireExtension(byte[] image)
    {
        if (image.Length > MaxBytes)
            throw new InvalidDataException("La foto pesa más de 10 MB. Elegí una más liviana.");

        if (image.AsSpan().StartsWith(JpegSignature)) return ".jpg";
        if (image.AsSpan().StartsWith(PngSignature)) return ".png";

        throw new InvalidDataException("La foto tiene que ser una imagen JPG o PNG.");
    }
}
