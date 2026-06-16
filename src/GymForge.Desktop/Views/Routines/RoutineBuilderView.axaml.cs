using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Routines;

namespace GymForge.Desktop.Views.Routines;

public partial class RoutineBuilderView : UserControl
{
    public RoutineBuilderView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is RoutineBuilderViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
