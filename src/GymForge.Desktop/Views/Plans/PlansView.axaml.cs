using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Plans;

namespace GymForge.Desktop.Views.Plans;

public partial class PlansView : UserControl
{
    public PlansView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PlansViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
