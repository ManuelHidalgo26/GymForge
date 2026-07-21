using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentAvalonia.Styling;
using GymForge.Desktop.Services;
using GymForge.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GymForge.Desktop;

public class App : Avalonia.Application
{
    public static IServiceProvider Services { get; internal set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Una excepción que escapa de un handler de UI se registra y se marca como
        // manejada: la app no se cierra, el usuario ve el estado actual y puede seguir.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Log.Error(e.Exception, "Excepción no controlada en el hilo de UI");
            e.Handled = true;
        };

        // Follow OS theme automatically; RequestedThemeVariant="Default" in AXAML
        // already does this, but we also sync our IsDarkTheme flag on the ViewModel.
        SyncThemeFlag();

        // Subscribe to OS theme changes (user switches Windows dark/light mode at runtime)
        if (PlatformSettings is { } ps)
            ps.ColorValuesChanged += (_, _) => SyncThemeFlag();

        // Aplicar el color de marca del gimnasio (persistido) antes de mostrar la ventana.
        if (Services is not null)
            ApplyBrandAccent(Services.GetRequiredService<SessionContext>().BrandColorHex);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var session = Services.GetRequiredService<SessionContext>();
            if (session.NeedsOnboarding)
                desktop.MainWindow = BuildOnboardingWindow(desktop);
            else
                desktop.MainWindow = BuildShellWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static MainWindow BuildShellWindow() =>
        new() { DataContext = Services.GetRequiredService<MainWindowViewModel>() };

    /// <summary>Ventana de onboarding; al completarse crea el shell y la reemplaza.</summary>
    private static Views.Onboarding.OnboardingWindow BuildOnboardingWindow(
        IClassicDesktopStyleApplicationLifetime desktop)
    {
        var vm = Services.GetRequiredService<ViewModels.Onboarding.OnboardingViewModel>();
        var window = new Views.Onboarding.OnboardingWindow { DataContext = vm };

        vm.Completed += () =>
        {
            var shell = BuildShellWindow();
            desktop.MainWindow = shell;
            shell.Show();
            window.Close();
        };

        return window;
    }

    /// <summary>Aplica el color de acento de la marca del gimnasio a FluentAvalonia
    /// (recalcula los tonos Light/Dark). Hex inválido → cae al indigo por defecto.</summary>
    public static void ApplyBrandAccent(string? hex)
    {
        if (Current is null) return;
        var faTheme = Current.Styles.OfType<FluentAvaloniaTheme>().FirstOrDefault();
        if (faTheme is null) return;

        faTheme.CustomAccentColor = Color.TryParse(hex, out var color)
            ? color
            : Color.Parse(SessionContext.DefaultBrandColorHex);
    }

    private void SyncThemeFlag()
    {
        if (Services is null) return;
        var vm = Services.GetService<MainWindowViewModel>();
        if (vm is null) return;

        // Determine effective theme: explicit override > OS preference
        bool isDark = RequestedThemeVariant == ThemeVariant.Dark ||
                      (RequestedThemeVariant == ThemeVariant.Default &&
                       PlatformSettings?.GetColorValues().ThemeVariant == PlatformThemeVariant.Dark);
        vm.IsDarkTheme = isDark;
    }
}
