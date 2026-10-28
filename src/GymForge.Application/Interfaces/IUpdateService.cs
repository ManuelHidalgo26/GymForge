namespace GymForge.Application.Interfaces;

/// <summary>Versión nueva encontrada en el origen de actualizaciones.</summary>
public sealed record AvailableUpdate(string Version);

public enum UpdateCheckStatus
{
    /// <summary>No hay origen de actualizaciones configurado: la auto-actualización no hace nada.</summary>
    FeedNotConfigured,
    /// <summary>Esta copia no fue instalada con el instalador (ejecución en desarrollo o exe portable).</summary>
    NotInstalled,
    UpToDate,
    Available,
    /// <summary>No se pudo consultar el origen (sin red, todavía sin releases, respuesta inválida...).</summary>
    Failed,
}

/// <summary>Resultado de buscar actualizaciones. <see cref="Update"/> solo viene con <see cref="UpdateCheckStatus.Available"/>.</summary>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, AvailableUpdate? Update = null)
{
    public static UpdateCheckResult FeedNotConfigured { get; } = new(UpdateCheckStatus.FeedNotConfigured);
    public static UpdateCheckResult NotInstalled { get; } = new(UpdateCheckStatus.NotInstalled);
    public static UpdateCheckResult UpToDate { get; } = new(UpdateCheckStatus.UpToDate);
    public static UpdateCheckResult Failed { get; } = new(UpdateCheckStatus.Failed);

    public static UpdateCheckResult Found(AvailableUpdate update) => new(UpdateCheckStatus.Available, update);
}

/// <summary>
/// Auto-actualización de la app. Es opcional y de mejor esfuerzo: buscar y descargar nunca lanzan ni
/// bloquean la operación del gimnasio; ante cualquier problema informan el resultado y la app sigue igual.
/// </summary>
public interface IUpdateService
{
    /// <summary>Versión de la app en ejecución, para mostrar en Configuración.</summary>
    string CurrentVersion { get; }

    /// <summary>Consulta el origen configurado. No lanza.</summary>
    Task<UpdateCheckResult> CheckAsync();

    /// <summary>Descarga la versión encontrada por <see cref="CheckAsync"/>. False si no se pudo. No lanza.</summary>
    Task<bool> DownloadAsync(AvailableUpdate update);

    /// <summary>Instala la versión descargada y reinicia la app ahora.</summary>
    /// <exception cref="InvalidOperationException">No hay una actualización descargada.</exception>
    void ApplyAndRestart(AvailableUpdate update);

    /// <summary>Programa la instalación de la versión descargada para cuando se cierre la app, sin reiniciarla.</summary>
    /// <exception cref="InvalidOperationException">No hay una actualización descargada.</exception>
    void ApplyOnExit(AvailableUpdate update);
}
