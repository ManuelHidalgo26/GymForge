using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GymForge.Desktop.Views.Shell;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        // Túnel: la paleta atiende sus teclas antes que el resto (p. ej. el IsCancel de un modal que haya detrás).
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        ResultsList.Tapped += OnResultTapped;
    }

    private CommandPaletteViewModel? Palette => DataContext as CommandPaletteViewModel;

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (Palette is not { } palette) return;

        switch (e.Key)
        {
            case Key.Escape:
                palette.CloseCommand.Execute(null);
                break;
            case Key.Enter:
                palette.ExecuteCommand.Execute(null);
                break;
            case Key.Down:
                palette.MoveSelection(1);
                break;
            case Key.Up:
                palette.MoveSelection(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnResultTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: PaletteCommand command })
            Palette?.ExecuteCommand.Execute(command);
    }
}
