using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using GymForge.Desktop.Views.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Desktop;

public class App : Avalonia.Application
{
    public static IServiceProvider Services { get; internal set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Follow OS theme automatically; RequestedThemeVariant="Default" in AXAML
        // already does this, but we also sync our IsDarkTheme flag on the ViewModel.
        SyncThemeFlag();

        // Subscribe to OS theme changes (user switches Windows dark/light mode at runtime)
        if (PlatformSettings is { } ps)
            ps.ColorValuesChanged += (_, _) => SyncThemeFlag();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Services.GetRequiredService<MainWindowViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
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
