namespace GymForge.Desktop.Services;

/// <summary>
/// De dónde sale la imagen de la foto de un socio. Hoy son archivos (elegir en el disco o pegar uno copiado);
/// una cámara en vivo se sumaría como otra fuente detrás de esta misma interfaz.
/// </summary>
public interface IImagePicker
{
    /// <summary>Abre el selector de archivos y devuelve los bytes de la imagen elegida; <c>null</c> si se cancela.</summary>
    Task<byte[]?> PickFileAsync(CancellationToken ct = default);

    /// <summary>Lee la imagen del portapapeles (un archivo copiado en el Explorador, o una imagen); <c>null</c> si no hay.</summary>
    Task<byte[]?> PasteAsync(CancellationToken ct = default);
}
