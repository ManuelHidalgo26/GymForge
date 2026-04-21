using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Dashboard;

namespace GymForge.Desktop.Views.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is DashboardViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
