using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using GymForge.Application.DTOs;
using GymForge.Desktop.ViewModels.Members;

namespace GymForge.Desktop.Views.Members;

public partial class MembersListView : UserControl
{
    public MembersListView()
    {
        InitializeComponent();
    }

    // Load data the first time the view is shown and whenever DataContext is set
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is MembersListViewModel vm && vm.Members.Count == 0)
            vm.LoadCommand.Execute(null);
    }

    // Double-click on a row opens the member detail
    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MembersListViewModel vm && vm.SelectedMember is { } member)
            vm.OpenDetailCommand.Execute(member);
    }
}
