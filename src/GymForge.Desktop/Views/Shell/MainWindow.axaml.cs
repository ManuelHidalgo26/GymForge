using Avalonia.Controls;

namespace GymForge.Desktop.Views.Shell;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) => SearchForUpdates();
    }

    /// <summary>
    /// Una sola búsqueda al abrir, ya con la UI viva y sin esperarla: el chequeo (red incluida) corre aparte y
    /// nunca demora ni rompe el arranque. UpdateViewModel absorbe cualquier falla.
    /// </summary>
    private void SearchForUpdates()
    {
        if (DataContext is MainWindowViewModel vm)
            _ = vm.Update.CheckAsync();
    }
}
