using Avalonia.Controls;
using Avalonia.Input;
using GymForge.Desktop.ViewModels.Members;

namespace GymForge.Desktop.Views.Members;

public partial class MembersListView : UserControl
{
    public MembersListView()
    {
        InitializeComponent();
    }

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MembersListViewModel vm)
            vm.SearchCommand.Execute(null);
    }
}
