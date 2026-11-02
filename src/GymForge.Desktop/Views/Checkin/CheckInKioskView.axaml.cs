using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Checkin;

namespace GymForge.Desktop.Views.Checkin;

public partial class CheckInKioskView : UserControl
{
    private CheckInKioskViewModel? _vm;
    private WedgeKeyboardListener? _scanner;

    public CheckInKioskView()
    {
        InitializeComponent();
        DniKeypad.Entered += (_, _) => _vm?.ManualCheckInCommand.Execute(null);

        // Enter en el campo de DNI = ingresar. Es un handler de túnel y NO un KeyBinding a propósito: Avalonia procesa
        // los KeyBindings antes de rutear la tecla, así que un KeyBinding se disparaba con el Enter final del lector
        // (aunque el listener lo trague) y registraba una carga manual "Desconocido" con el primer carácter del código.
        // Un handler de túnel corre después del listener de la vista y respeta su Handled.
        DniInput.AddHandler(KeyDownEvent, OnDniKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnDniKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        _vm?.ManualCheckInCommand.Execute(null);
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.FocusRequested -= FocusInput;

        _vm = DataContext as CheckInKioskViewModel;
        if (_vm is not null)
        {
            _vm.FocusRequested += FocusInput;
            _vm.LoadCommand.Execute(null);   // accesos del día
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // El lector de códigos tipea como un teclado: se lo escucha en toda la pantalla, sin depender del foco.
        _scanner = WedgeKeyboardListener.ForWindowOf(this);
        _scanner.Scanned += OnScanned;
        FocusInput();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        _scanner?.Dispose();
        _scanner = null;
    }

    // El primer carácter de la ráfaga pudo caer en el campo de DNI: el VM lo limpia al procesar el escaneo.
    private void OnScanned(string code)
    {
        if (_vm is not null) _ = _vm.ProcessScanAsync(code);
    }

    // Enfocar el campo de DNI para escanear/tipear seguido y confirmar con Enter, sin mouse.
    private void FocusInput() => Dispatcher.UIThread.Post(() => DniInput?.Focus());
}
