using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Settings;

namespace GymForge.Desktop.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is SettingsViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
