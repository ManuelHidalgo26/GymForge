using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Routines;

namespace GymForge.Desktop.Views.Routines;

public partial class ExerciseLibraryView : UserControl
{
    public ExerciseLibraryView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ExerciseLibraryViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
