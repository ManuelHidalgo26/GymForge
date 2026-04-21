using CommunityToolkit.Mvvm.ComponentModel;

namespace GymForge.Desktop.ViewModels.Shared;

/// <summary>
/// Stand-in ViewModel for sections not yet implemented.
/// Shows a "coming soon" panel in PlaceholderView.
/// </summary>
public sealed class PlaceholderViewModel : ObservableObject
{
    public string Title   { get; }
    public string Subtitle { get; }
    public string Icon    { get; }

    public PlaceholderViewModel(string title, string subtitle = "Este módulo estará disponible en próximos sprints.", string icon = "Clock")
    {
        Title    = title;
        Subtitle = subtitle;
        Icon     = icon;
    }
}
