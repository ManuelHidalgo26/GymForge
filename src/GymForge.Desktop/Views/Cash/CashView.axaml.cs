using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Cash;

namespace GymForge.Desktop.Views.Cash;

public partial class CashView : UserControl
{
    public CashView()
    {
        InitializeComponent();
        PinKeypad.Entered += (_, _) => (DataContext as CashViewModel)?.LoginCommand.Execute(null);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is CashViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
