using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Classes;

namespace GymForge.Desktop.Views.Classes;

public partial class ClassesView : UserControl
{
    public ClassesView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ClassesViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
