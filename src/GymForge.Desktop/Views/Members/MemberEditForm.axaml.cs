using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using GymForge.Desktop.ViewModels.Members;

namespace GymForge.Desktop.Views.Members;

/// <summary>
/// Formulario de edición de los datos del socio (pestaña Datos de la ficha). Es solo presentación: se enlaza al
/// <c>MemberEditViewModel</c> que le da la ficha como DataContext.
/// </summary>
public partial class MemberEditForm : UserControl
{
    private MemberEditViewModel? _vm;

    public MemberEditForm()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rewire();
    }

    // El formulario vive oculto dentro de la ficha y se muestra con "Editar": cuando el VM entra en edición,
    // el foco va al primer campo (teclado y lector de pantalla arrancan donde corresponde).
    private void Rewire()
    {
        if (_vm is not null) _vm.PropertyChanged -= OnViewModelChanged;
        _vm = DataContext as MemberEditViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnViewModelChanged;
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MemberEditViewModel.IsEditing) && _vm is { IsEditing: true })
            Dispatcher.UIThread.Post(() => FirstNameInput.Focus(), DispatcherPriority.Input);
    }
}
