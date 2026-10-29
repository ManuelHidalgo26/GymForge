using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Collections;

namespace GymForge.Desktop.Views.Collections;

public partial class CollectionsView : UserControl
{
    public CollectionsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CollectionsViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
