using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace GymForge.Desktop.Services;

/// <summary>Elegir o pegar la foto con lo que da el sistema: el selector de archivos y el portapapeles de la ventana principal.</summary>
public sealed class AvaloniaImagePicker : IImagePicker
{
    // Formato en el que Windows deja las imágenes copiadas desde el navegador o una captura de pantalla.
    private const string PngClipboardFormat = "PNG";

    public async Task<byte[]?> PickFileAsync(CancellationToken ct = default)
    {
        if (MainWindow()?.StorageProvider is not { } storage) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí la foto del socio",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Imágenes (JPG, PNG)") { Patterns = ["*.jpg", "*.jpeg", "*.png"] }],
        });

        return files.FirstOrDefault() is { } file ? await ReadAsync(file, ct) : null;
    }

    public async Task<byte[]?> PasteAsync(CancellationToken ct = default)
    {
        if (MainWindow()?.Clipboard is not { } clipboard) return null;

        var formats = await clipboard.GetFormatsAsync();

        // Un archivo copiado en el Explorador (Ctrl+C sobre la foto).
        if (formats.Contains(DataFormats.Files)
            && await clipboard.GetDataAsync(DataFormats.Files) is IEnumerable<IStorageItem> items
            && items.OfType<IStorageFile>().FirstOrDefault() is { } file)
        {
            return await ReadAsync(file, ct);
        }

        // Una imagen copiada (captura de pantalla, "Copiar imagen" del navegador).
        return formats.Contains(PngClipboardFormat) && await clipboard.GetDataAsync(PngClipboardFormat) is byte[] png
            ? png
            : null;
    }

    private static async Task<byte[]> ReadAsync(IStorageFile file, CancellationToken ct)
    {
        await using var stream = await file.OpenReadAsync();
        return await PhotoImage.ReadCappedAsync(stream, ct);
    }

    private static Window? MainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
}
