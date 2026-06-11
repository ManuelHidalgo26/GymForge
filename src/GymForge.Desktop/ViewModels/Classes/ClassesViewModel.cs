using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.UseCases.Classes;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Classes;

/// <summary>Catálogo de clases del gimnasio (v1: definición; horarios y reservas después).</summary>
public partial class ClassesViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;

    [ObservableProperty] private ObservableCollection<ClassDto> _classes = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _durationMin = 60;
    [ObservableProperty] private int _capacity = 20;

    public bool IsEmpty => !IsLoading && Classes.Count == 0;

    public ClassesViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    partial void OnClassesChanged(ObservableCollection<ClassDto> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Classes = new ObservableCollection<ClassDto>(
                await _mediator.Send(new GetClassesQuery(_session.CompanyId), ct));
        }
        finally { IsLoading = false; }
    }

    [RelayCommand] private void OpenForm() { ErrorMessage = null; IsFormOpen = true; }
    [RelayCommand] private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new CreateClassCommand(_session.CompanyId, Name, DurationMin, Capacity));
            Name = string.Empty;
            DurationMin = 60;
            Capacity = 20;
            IsFormOpen = false;
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
