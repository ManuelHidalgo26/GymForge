using Avalonia;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.DTOs;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
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
    Plans,
    Classes,
    Routines,
    Products,
    Reports,
    Settings
}

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IServiceProvider _sp;
    private readonly SessionContext _session;

    public SessionContext Session => _session;

    // Singleton cache per top-level section
    private readonly Dictionary<NavSection, object> _vmCache = new();

    // Simple navigation history for back-button support
    private readonly Stack<object> _history = new();

    public MainWindowViewModel(IServiceProvider sp)
    {
        _sp = sp;
        _session = sp.GetRequiredService<SessionContext>();
        GymName = _session.GymName;
        CurrentSiteName = _session.CurrentSite?.Name ?? "—";
        _session.PropertyChanged += OnSessionChanged;

        Navigate(NavSection.Dashboard);

        // Wire MembersListViewModel events
        var membersList = _sp.GetRequiredService<MembersListViewModel>();
        membersList.OpenDetailRequested  += OpenMemberDetail;
        membersList.CreateMemberRequested += OpenCreateMember;

        // Acciones rápidas del Dashboard
        var dashboard = _sp.GetRequiredService<DashboardViewModel>();
        dashboard.CreateMemberRequested += OpenCreateMember;
        dashboard.CheckInRequested += () => Navigate(NavSection.Access);
        dashboard.ChargeRequested += () => Navigate(NavSection.Cash);
    }

    private void OnSessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SessionContext.GymName))
            GymName = _session.GymName;

        if (e.PropertyName == nameof(SessionContext.CurrentSite))
        {
            CurrentSiteName = _session.CurrentSite?.Name ?? "—";
            // Cambiar de sede invalida las vistas cacheadas (datos por sede).
            _vmCache.Clear();
            Navigate(CurrentSection);
        }
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

            // Caja registradora: turno, movimientos y arqueo
            NavSection.Cash      => Cached(section, () => _sp.GetRequiredService<CashViewModel>()),

            NavSection.Plans     => Cached(section, () => _sp.GetRequiredService<ViewModels.Plans.PlansViewModel>()),

            NavSection.Classes   => Cached(section, () => _sp.GetRequiredService<ViewModels.Classes.ClassesViewModel>()),
            NavSection.Routines  => Cached(section, () => _sp.GetRequiredService<ViewModels.Routines.ExerciseLibraryViewModel>()),
            NavSection.Products  => Cached(section, () => new PlaceholderViewModel(
                                        "Módulo Productos", "Kiosk de ventas y stock — Sprint 3.", "Tag")),
            NavSection.Reports   => Cached(section, () => _sp.GetRequiredService<ViewModels.Reports.ReportsViewModel>()),
            NavSection.Settings  => Cached(section, () => _sp.GetRequiredService<ViewModels.Settings.SettingsViewModel>()),
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
