using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Members;

namespace GymForge.Desktop.Views.Members;

public partial class CreateMemberView : UserControl
{
    private CreateMemberViewModel? _vm;
    private WedgeKeyboardListener? _scanner;

    public CreateMemberView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.CredentialFocusRequested -= FocusCredential;

        _vm = DataContext as CreateMemberViewModel;
        if (_vm is not null) _vm.CredentialFocusRequested += FocusCredential;
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        // Un código leído por el lector va a la credencial. Se lo escucha en toda la pantalla (no solo con el
        // campo enfocado) para que el Enter final del lector no dispare "Guardar socio" (botón por defecto).
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
        if (_vm is not null) _vm.TagSerial = code;
    }

    private void FocusCredential() => Dispatcher.UIThread.Post(() => CredentialInput.Focus());
}
