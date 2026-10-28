using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.Interfaces;
using Serilog;

namespace GymForge.Desktop.ViewModels.Shared;

/// <summary>
/// Estado de la auto-actualización para el shell (aviso "hay una actualización lista") y para Configuración
/// (versión actual y "Buscar actualizaciones"). Busca al abrir la app, descarga en silencio y deja que el
/// usuario decida cuándo reiniciar: nunca interrumpe un cobro o un check-in. Todo es de mejor esfuerzo: un
/// origen caído o inexistente solo deja un mensaje en Configuración.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private const string SearchFailedMessage =
        "No se pudo buscar actualizaciones (sin conexión o sin versiones publicadas). Se reintenta al abrir GymForge.";

    private readonly IUpdateService _updates;
    private AvailableUpdate? _ready;

    public UpdateViewModel(IUpdateService updates) => _updates = updates;

    public string CurrentVersion => _updates.CurrentVersion;

    /// <summary>La nueva versión ya está descargada y lista para instalar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isUpdateReady;

    [ObservableProperty] private string? _updateVersion;

    /// <summary>El usuario eligió "Después": el aviso se oculta pero la instalación queda programada.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBannerVisible))]
    private bool _isBannerDismissed;

    [ObservableProperty] private bool _isChecking;

    /// <summary>Última novedad para mostrar en Configuración (vacío hasta la primera búsqueda).</summary>
    [ObservableProperty] private string _statusMessage = string.Empty;

    public bool IsBannerVisible => IsUpdateReady && !IsBannerDismissed;

    /// <summary>Busca y descarga una actualización. No lanza ni se superpone con otra búsqueda en curso.</summary>
    [RelayCommand]
    public async Task CheckAsync()
    {
        if (IsChecking || IsUpdateReady) return;

        IsChecking = true;
        StatusMessage = "Buscando actualizaciones…";
        try
        {
            var result = await _updates.CheckAsync();
            if (result is { Status: UpdateCheckStatus.Available, Update: { } update })
                await DownloadAsync(update);
            else
                StatusMessage = MessageFor(result.Status);
        }
        // Se lanza sin esperarla desde el shell: nada que escape de acá puede llegar a la UI.
        catch (Exception ex)
        {
            Log.Warning(ex, "Actualizaciones: falló la búsqueda");
            StatusMessage = SearchFailedMessage;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void RestartNow()
    {
        if (_ready is not { } update) return;

        try
        {
            _updates.ApplyAndRestart(update);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Actualizaciones: no se pudo aplicar la versión {Version}", update.Version);
            StatusMessage = "No se pudo iniciar la actualización. Probá de nuevo o cerrá y volvé a abrir GymForge.";
        }
    }

    [RelayCommand]
    private void Later()
    {
        if (_ready is not { } update) return;

        IsBannerDismissed = true;
        try
        {
            _updates.ApplyOnExit(update);
            StatusMessage = $"La versión {update.Version} se instalará al cerrar GymForge.";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Actualizaciones: no se pudo programar la versión {Version}", update.Version);
            StatusMessage = $"La versión {update.Version} está descargada pero no se pudo programar su instalación.";
        }
    }

    private async Task DownloadAsync(AvailableUpdate update)
    {
        StatusMessage = $"Descargando la versión {update.Version}…";
        if (!await _updates.DownloadAsync(update))
        {
            StatusMessage = $"Hay una versión nueva ({update.Version}) pero no se pudo descargar. Se reintenta al abrir GymForge.";
            return;
        }

        _ready = update;
        UpdateVersion = update.Version;
        IsBannerDismissed = false;
        IsUpdateReady = true;
        StatusMessage = $"La versión {update.Version} está lista para instalar.";
    }

    private static string MessageFor(UpdateCheckStatus status) => status switch
    {
        UpdateCheckStatus.UpToDate => "Tenés la última versión.",
        UpdateCheckStatus.FeedNotConfigured =>
            "Las actualizaciones automáticas están desactivadas: no hay un origen de actualizaciones configurado.",
        UpdateCheckStatus.NotInstalled =>
            "Esta copia no se instaló con el instalador de GymForge, por eso no se actualiza sola.",
        _ => SearchFailedMessage,
    };
}
