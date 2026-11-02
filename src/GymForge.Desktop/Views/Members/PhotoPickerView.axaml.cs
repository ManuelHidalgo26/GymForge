using Avalonia.Controls;

namespace GymForge.Desktop.Views.Members;

/// <summary>
/// El cuadro de la foto del socio (vista previa + "Elegir archivo" / "Pegar imagen" + errores). Es solo presentación:
/// se enlaza a <c>PhotoPickerViewModel</c> vía el DataContext que le da el alta o la ficha.
/// </summary>
public partial class PhotoPickerView : UserControl
{
    public PhotoPickerView()
    {
        InitializeComponent();
    }
}
