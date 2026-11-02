using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GymForge.Desktop.Views.Members;

public partial class MemberDetailView : UserControl
{
    public MemberDetailView()
    {
        InitializeComponent();
    }

    // "Escanear": deja el foco en el campo de la nueva credencial. El lector de códigos tipea como un teclado y su
    // Enter final dispara "Asignar" (KeyBinding del campo); con el campo enfocado, el primer carácter cae ahí y no en otro.
    private void OnScanCredentialClick(object? sender, RoutedEventArgs e) => CredentialInput.Focus();
}
