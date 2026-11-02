using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.ViewModels.Members;

/// <summary>
/// El cuadro de la foto del socio: elegir un archivo o pegar una imagen, validarla y mostrar la vista previa.
/// Lo comparten el alta (guarda al crear el socio) y la ficha (guarda al elegir); guardar queda en quien lo usa.
/// </summary>
public partial class PhotoPickerViewModel : ObservableObject
{
    private readonly IImagePicker _picker;

    [ObservableProperty] private Bitmap? _preview;
    [ObservableProperty] private string? _message;

    /// <summary>La imagen elegida que todavía no se guardó; <c>null</c> si no hay o ya se guardó.</summary>
    public byte[]? PendingImage { get; private set; }

    public bool HasPendingImage => PendingImage is not null;

    /// <summary>Se eligió una imagen válida (ya es <see cref="PendingImage"/> y se ve en <see cref="Preview"/>).</summary>
    public event Action<byte[]>? ImageChosen;

    public PhotoPickerViewModel(IImagePicker picker) => _picker = picker;

    [RelayCommand]
    private Task PickAsync(CancellationToken ct = default) =>
        ChooseAsync(_picker.PickFileAsync, noImageMessage: null, ct);

    [RelayCommand]
    private Task PasteAsync(CancellationToken ct = default) =>
        ChooseAsync(_picker.PasteAsync, "No hay ninguna imagen en el portapapeles.", ct);

    /// <summary>Descarta la imagen elegida y la vista previa.</summary>
    [RelayCommand]
    private void Clear()
    {
        SetPending(null);
        Preview = null;
        Message = null;
    }

    /// <summary>La imagen pendiente ya se guardó: no se vuelve a guardar, y la vista previa sigue mostrándola.</summary>
    public void MarkSaved() => SetPending(null);

    /// <summary>Muestra la foto que el socio ya tiene guardada, si el archivo existe.</summary>
    public void ShowSaved(string? path) => Preview = PhotoBitmap.TryLoad(path);

    private async Task ChooseAsync(
        Func<CancellationToken, Task<byte[]?>> acquire, string? noImageMessage, CancellationToken ct)
    {
        Message = null;
        byte[]? image;
        try
        {
            image = await acquire(ct);
            if (image is not null) PhotoImage.RequireExtension(image);
        }
        catch (Exception ex)
        {
            Message = ex is InvalidDataException ? ex.Message : $"No se pudo leer la imagen: {ex.Message}";
            return;
        }

        if (image is null)
        {
            Message = noImageMessage;
            return;
        }

        SetPending(image);
        Preview = PhotoBitmap.TryDecode(image);
        ImageChosen?.Invoke(image);
    }

    private void SetPending(byte[]? image)
    {
        PendingImage = image;
        OnPropertyChanged(nameof(HasPendingImage));
    }
}
