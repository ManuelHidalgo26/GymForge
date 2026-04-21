using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Charges;

namespace GymForge.Desktop.Views.Charges;

public partial class ChargesView : UserControl
{
    public ChargesView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ChargesViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
