using Avalonia.Controls;
using GymForge.Desktop.ViewModels.Products;

namespace GymForge.Desktop.Views.Products;

public partial class ProductsView : UserControl
{
    public ProductsView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ProductsViewModel vm)
            vm.LoadCommand.Execute(null);
    }
}
