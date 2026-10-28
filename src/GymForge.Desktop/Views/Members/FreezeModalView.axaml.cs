using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GymForge.Desktop.Views.Members;

public partial class FreezeModalView : UserControl
{
    public FreezeModalView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        // Al abrir el overlay el foco sigue en el botón "Congelar" de la grilla (queda tapado):
        // lo traemos a la fecha, el campo que el cajero casi siempre ajusta.
        Dispatcher.UIThread.Post(() => FocusFirstField(retry: true));
    }

    private void FocusFirstField(bool retry)
    {
        // El DatePicker delega el foco en su botón interno, así que enfocamos ese.
        var target = ReactivationPicker.GetVisualDescendants().OfType<Button>().FirstOrDefault();
        target?.Focus();

        // Si el overlay todavía no era visible en este turno, reintenta una sola vez.
        if (target?.IsFocused != true && retry)
            Dispatcher.UIThread.Post(() => FocusFirstField(retry: false), DispatcherPriority.Background);
    }
}
