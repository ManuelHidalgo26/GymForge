using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymForge.Desktop.ViewModels.Checkin;

namespace GymForge.Desktop.Views.Checkin;

public partial class CheckInKioskView : UserControl
{
    private CheckInKioskViewModel? _vm;

    public CheckInKioskView()
    {
        InitializeComponent();
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
        FocusInput();
    }

    // Enfocar el campo de DNI para escanear/tipear seguido y confirmar con Enter, sin mouse.
    private void FocusInput() => Dispatcher.UIThread.Post(() => DniInput?.Focus());
}
