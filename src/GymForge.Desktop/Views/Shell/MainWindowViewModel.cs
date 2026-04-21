using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    [ObservableProperty]
    private NavSection _currentSection = NavSection.Dashboard;

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

    [RelayCommand]
    private void Navigate(NavSection section) => CurrentSection = section;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    [RelayCommand]
    private void OpenCommandPalette() => IsCommandPaletteOpen = true;

    [RelayCommand]
    private void CloseCommandPalette() => IsCommandPaletteOpen = false;
}

public partial class ShellViewModel : ObservableObject { }
