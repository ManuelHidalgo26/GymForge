using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GymForge.Desktop.Views.Shell;

/// <summary>
/// Estado de la paleta de comandos (Ctrl+K): abierta/cerrada, texto de búsqueda, resultados filtrados y selección.
/// Solo navega y dispara acciones que ya existen; no escribe datos.
/// </summary>
public partial class CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands) : ObservableObject
{
    public ObservableCollection<PaletteCommand> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    private bool _isOpen;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteCommand? _selected;

    public bool HasNoResults => Results.Count == 0;

    [RelayCommand]
    private void Open()
    {
        Query = string.Empty;
        Refresh();
        IsOpen = true;
    }

    /// <summary>Solo se puede ejecutar abierta: con la paleta cerrada Escape no se "consume" y le llega a quien corresponda.</summary>
    [RelayCommand(CanExecute = nameof(IsOpen))]
    private void Close()
    {
        IsOpen = false;
        Query = string.Empty;
    }

    /// <summary>Ejecuta el comando dado (click) o, si no hay, el seleccionado (Enter). Cierra primero para que el foco vuelva antes de navegar.</summary>
    [RelayCommand]
    private void Execute(PaletteCommand? command)
    {
        var target = command ?? Selected;
        if (target is null) return;

        Close();
        target.Run();
    }

    /// <summary>Mueve la selección con ↑/↓ y da la vuelta en los extremos.</summary>
    public void MoveSelection(int delta)
    {
        if (Results.Count == 0) return;

        var current = Selected is null ? -1 : Results.IndexOf(Selected);
        var next = current < 0
            ? (delta > 0 ? 0 : Results.Count - 1)
            : (current + delta + Results.Count) % Results.Count;
        Selected = Results[next];
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        foreach (var command in PaletteSearch.Filter(commands, Query))
            Results.Add(command);

        Selected = Results.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoResults));
    }
}
