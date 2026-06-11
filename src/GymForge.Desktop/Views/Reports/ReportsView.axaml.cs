using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Reports;

namespace GymForge.Desktop.Views.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ReportsViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
