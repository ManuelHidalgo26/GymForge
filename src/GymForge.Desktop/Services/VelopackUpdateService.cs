using System.Reflection;
using Avalonia.Controls.ApplicationLifetimes;
using GymForge.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace GymForge.Desktop.Services;

/// <summary>
/// Auto-actualización con Velopack. El origen sale de <see cref="ILocalSettings.UpdateFeedUrl"/> (no hay ningún
/// repositorio fijo en el código): sin origen configurado, o con la app sin instalar (<c>dotnet run</c>, exe
/// portable), no hace nada. Una URL de github.com se trata como repositorio de releases; cualquier otra
/// (servidor web o carpeta de red) como carpeta de archivos de Velopack. Es de mejor esfuerzo: un origen que
/// todavía no existe, la falta de red o una respuesta inválida se informan como <see cref="UpdateCheckStatus.Failed"/>
/// y nunca propagan una excepción.
/// </summary>
public sealed partial class VelopackUpdateService : IUpdateService
{
    private readonly ILocalSettings _settings;
    private readonly ILogger<VelopackUpdateService> _log;
    private PendingUpdate? _pending;

    public VelopackUpdateService(ILocalSettings settings, ILogger<VelopackUpdateService> log)
    {
        _settings = settings;
        _log = log;
    }

    public string CurrentVersion { get; } = ReadAssemblyVersion();

    public async Task<UpdateCheckResult> CheckAsync()
    {
        var feedUrl = UpdateFeed.Resolve(_settings);
        if (string.IsNullOrEmpty(feedUrl)) return UpdateCheckResult.FeedNotConfigured;

        if (!UpdateFeed.IsAllowedSource(feedUrl, out _))
        {
            LogInsecureFeed(feedUrl);
            return UpdateCheckResult.FeedInsecure;
        }

        try
        {
            // Fuera del hilo de UI: armar el UpdateManager lee disco y la consulta sale a la red.
            return await Task.Run(() => CheckFeedAsync(feedUrl));
        }
        // Contrato de mejor esfuerzo: el origen puede no existir todavía, no haber red o devolver basura.
        catch (Exception ex)
        {
            LogCheckFailed(ex, feedUrl);
            return UpdateCheckResult.Failed;
        }
    }

    public async Task<bool> DownloadAsync(AvailableUpdate update)
    {
        if (PendingFor(update) is not { } pending)
        {
            LogNothingToDownload(update.Version);
            return false;
        }

        try
        {
            await Task.Run(() => pending.Manager.DownloadUpdatesAsync(pending.Info));
            return true;
        }
        catch (Exception ex)
        {
            LogDownloadFailed(ex, update.Version);
            return false;
        }
    }

    public void ApplyAndRestart(AvailableUpdate update)
    {
        var pending = RequirePending(update);
        pending.Manager.WaitExitThenApplyUpdates(pending.Info.TargetFullRelease, silent: true, restart: true);

        // Cierre ordenado (flush del log, DbContext): el updater espera a que este proceso termine.
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }

    public void ApplyOnExit(AvailableUpdate update)
    {
        var pending = RequirePending(update);
        pending.Manager.WaitExitThenApplyUpdates(pending.Info.TargetFullRelease, silent: true, restart: false);
    }

    private async Task<UpdateCheckResult> CheckFeedAsync(string feedUrl)
    {
        // Program.cs arranca Velopack con VelopackApp.Run(); sin eso (host de tests, herramientas) el
        // UpdateManager ni siquiera se puede construir y tampoco hay una instalación que actualizar.
        if (!VelopackLocator.IsCurrentSet) return UpdateCheckResult.NotInstalled;

        var manager = CreateManager(feedUrl);
        if (!manager.IsInstalled) return UpdateCheckResult.NotInstalled;

        var info = await manager.CheckForUpdatesAsync();
        if (info is null)
        {
            _pending = null;
            return UpdateCheckResult.UpToDate;
        }

        _pending = new PendingUpdate(manager, info);
        return UpdateCheckResult.Found(new AvailableUpdate(info.TargetFullRelease.Version.ToString()));
    }

    private static UpdateManager CreateManager(string feedUrl) =>
        IsGitHubRepository(feedUrl)
            ? new UpdateManager(new GithubSource(feedUrl, accessToken: null, prerelease: false))
            : new UpdateManager(feedUrl);

    private static bool IsGitHubRepository(string feedUrl) =>
        Uri.TryCreate(feedUrl, UriKind.Absolute, out var uri)
        && uri.Host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase);

    private PendingUpdate? PendingFor(AvailableUpdate update) =>
        _pending is { } pending && pending.Info.TargetFullRelease.Version.ToString() == update.Version
            ? pending
            : null;

    private PendingUpdate RequirePending(AvailableUpdate update) =>
        PendingFor(update)
        ?? throw new InvalidOperationException(
            $"No hay una actualización descargada para la versión {update.Version}.");

    /// <summary>Versión informativa del ensamblado (la del instalador), sin el hash de build tras el '+'.</summary>
    private static string ReadAssemblyVersion()
    {
        var informational = typeof(VelopackUpdateService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return informational?.Split('+')[0] ?? "desconocida";
    }

    private sealed record PendingUpdate(UpdateManager Manager, UpdateInfo Info);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Actualizaciones: no se pudo consultar el origen {FeedUrl} (sin red o sin releases publicados todavía)")]
    private partial void LogCheckFailed(Exception ex, string feedUrl);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Actualizaciones: se ignoró el origen {FeedUrl} por no ser seguro (se exige https o una carpeta)")]
    private partial void LogInsecureFeed(string feedUrl);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Actualizaciones: no se pudo descargar la versión {Version}")]
    private partial void LogDownloadFailed(Exception ex, string version);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Actualizaciones: se pidió descargar la versión {Version} pero la última búsqueda no la encontró")]
    private partial void LogNothingToDownload(string version);
}
