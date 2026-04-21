using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Desktop.ViewModels.Checkin;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.ViewModels.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Desktop.Views.Shell;

public enum NavSection
{
    Dashboard,
    Members,
    Access,
    Cash,
    Classes,
    Routines,
    Products,
    Reports,
    Settings
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _sp;

    // Cache singletons per section (Access/CheckIn is always re-created so it resets state)
    private readonly Dictionary<NavSection, object> _vmCache = new();

    public MainWindowViewModel(IServiceProvider sp)
    {
        _sp = sp;
        // Start on Dashboard
        Navigate(NavSection.Dashboard);
    }

    [ObservableProperty]
    private NavSection _currentSection = NavSection.Dashboard;

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private bool _isSidebarCollapsed;

    [ObservableProperty]
    private bool _isCommandPaletteOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _gymName = "GymForge";

    [ObservableProperty]
    private string _currentSiteName = "Sede Principal";

    [ObservableProperty]
    private bool _isDarkTheme;

    // ── Navigation ──────────────────────────────────────────────────────────

    [RelayCommand]
    private void Navigate(NavSection section)
    {
        CurrentSection = section;

        CurrentViewModel = section switch
        {
            NavSection.Dashboard => Cached(section, () => _sp.GetRequiredService<DashboardViewModel>()),
            NavSection.Members   => Cached(section, () => _sp.GetRequiredService<MembersListViewModel>()),

            // CheckIn Kiosk resets on every navigation (clears countdown / state)
            NavSection.Access    => _sp.GetRequiredService<CheckInKioskViewModel>(),

            NavSection.Cash      => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Caja", "Cobros, pagos y asignaciones — Sprint 2.", "Money")),
            NavSection.Classes   => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Clases", "Horarios, turnos y reservas — Sprint 2.", "Calendar")),
            NavSection.Routines  => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Rutinas", "Planes de entrenamiento — Sprint 3.", "Globe")),
            NavSection.Products  => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Productos", "Kiosk de ventas y stock — Sprint 3.", "Tag")),
            NavSection.Reports   => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Reportes", "Estadísticas y exportación PDF — Sprint 3.", "Document")),
            NavSection.Settings  => Cached(section, () => new PlaceholderViewModel(
                                        "Configuración", "Parámetros del sistema — Sprint 2.", "Settings")),
            _                    => null
        };
    }

    // ── Sidebar ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    // ── Command Palette ───────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenCommandPalette() => IsCommandPaletteOpen = true;

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;

    // ── Theme ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ToggleTheme()
    {
        if (Application.Current is not { } app) return;

        var goingDark = app.RequestedThemeVariant != ThemeVariant.Dark;
        app.RequestedThemeVariant = goingDark ? ThemeVariant.Dark : ThemeVariant.Light;
        IsDarkTheme = goingDark;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private object Cached(NavSection key, Func<object> factory)
    {
        if (!_vmCache.TryGetValue(key, out var vm))
        {
            vm = factory();
            _vmCache[key] = vm;
        }
        return vm;
    }
}
