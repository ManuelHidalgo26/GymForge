using Avalonia.Controls;
using Avalonia.Interactivity;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;

namespace GymForge.Desktop.Views.Cash;

public partial class RegisterSaleView : UserControl
{
    private WedgeKeyboardListener? _scanner;

    public RegisterSaleView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Solo mientras el modal está abierto: un código leído elige el producto o el socio.
        _scanner = WedgeKeyboardListener.ForWindowOf(this);
        _scanner.Scanned += OnScanned;
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _scanner?.Dispose();
        _scanner = null;
    }

    private void OnScanned(string code)
    {
        if (DataContext is RegisterSaleViewModel vm) _ = vm.ScanAsync(code);
    }
}
