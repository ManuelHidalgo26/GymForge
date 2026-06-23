using System.IO;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GymForge.Application.DTOs;
using GymForge.Desktop.ViewModels.Members;

namespace GymForge.Desktop.Views.Members;

public partial class MembersListView : UserControl
{
    private MembersListViewModel? _subscribed;

    public MembersListView()
    {
        InitializeComponent();
    }

    // Load data the first time the view is shown and whenever DataContext is set
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.ImportRequested -= OnImportRequested;

        if (DataContext is MembersListViewModel vm)
        {
            _subscribed = vm;
            vm.ImportRequested += OnImportRequested;
            if (vm.Members.Count == 0)
                vm.LoadCommand.Execute(null);
        }
    }

    private async void OnImportRequested()
    {
        if (DataContext is not MembersListViewModel vm) return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Elegí el archivo CSV con el padrón de socios",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Planilla CSV") { Patterns = new[] { "*.csv", "*.txt" } },
            },
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        await vm.ImportFromCsvAsync(text);
    }

    // Double-click on a row opens the member detail
    private void DataGrid_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MembersListViewModel vm && vm.SelectedMember is { } member)
            vm.OpenDetailCommand.Execute(member);
    }
}
