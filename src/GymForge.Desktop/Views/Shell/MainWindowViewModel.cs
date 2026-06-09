using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Desktop.ViewModels.Charges;
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

    // Singleton cache per top-level section
    private readonly Dictionary<NavSection, object> _vmCache = new();

    // Simple navigation history for back-button support
    private readonly Stack<object> _history = new();

    public MainWindowViewModel(IServiceProvider sp)
    {
        _sp = sp;
        Navigate(NavSection.Dashboard);

        // Wire MembersListViewModel events
        var membersList = _sp.GetRequiredService<MembersListViewModel>();
        membersList.OpenDetailRequested  += OpenMemberDetail;
        membersList.CreateMemberRequested += OpenCreateMember;
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

    [ObservableProperty]
    private bool _canGoBack;

    // ── Primary navigation (sidebar) ─────────────────────────────────────────

    [RelayCommand]
    private void Navigate(NavSection section)
    {
        CurrentSection = section;
        _history.Clear();
        CanGoBack = false;

        CurrentViewModel = section switch
        {
            NavSection.Dashboard => Cached(section, () => _sp.GetRequiredService<DashboardViewModel>()),
            NavSection.Members   => Cached(section, () => _sp.GetRequiredService<MembersListViewModel>()),

            // CheckIn resets on every navigation (clears countdown/state)
            NavSection.Access    => _sp.GetRequiredService<CheckInKioskViewModel>(),

            // Cobros: vista de charges filtrada por site (no por member)
            NavSection.Cash      => Cached(section, () => _sp.GetRequiredService<ChargesViewModel>()),

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

    // ── Nested navigation (detail views) ─────────────────────────────────────

    private void OpenMemberDetail(MemberDto member)
    {
        var detailVm = _sp.GetRequiredService<MemberDetailViewModel>();
        detailVm.BackRequested += GoBack;
        _ = detailVm.LoadAsync(member);

        Push(detailVm);
    }

    [RelayCommand]
    private void GoBack()
    {
        if (_history.TryPop(out var prev))
        {
            CurrentViewModel = prev;
            CanGoBack = _history.Count > 0;
        }
    }

    private void Push(object vm)
    {
        _history.Push(CurrentViewModel!);
        CurrentViewModel = vm;
        CanGoBack = true;
    }

    // ── New Member form ───────────────────────────────────────────────────────

    // Called from sidebar event or directly
    private void OpenCreateMember()
    {
        var createVm = _sp.GetRequiredService<CreateMemberViewModel>();
        createVm.Cancelled += GoBack;
        createVm.MemberCreated += dto =>
        {
            GoBack();
            // Refresh list
            if (_sp.GetRequiredService<MembersListViewModel>() is { } listVm)
                _ = listVm.LoadAsync();
        };
        Push(createVm);
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

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
        if (Avalonia.Application.Current is not { } app) return;
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
