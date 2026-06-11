using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Plans;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Plans;

/// <summary>Administración de planes de membresía: listar, crear, activar/desactivar.</summary>
public partial class PlansViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;

    [ObservableProperty] private ObservableCollection<MembershipTypeDto> _plans = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Formulario "Nuevo plan"
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private decimal _price;
    [ObservableProperty] private MembershipBasis _basis = MembershipBasis.Renewal;
    [ObservableProperty] private int _durationValue = 1;
    [ObservableProperty] private string _durationUnit = "Month";

    public IReadOnlyList<MembershipBasis> BasisOptions { get; } = Enum.GetValues<MembershipBasis>().ToList();
    public IReadOnlyList<string> DurationUnits { get; } = ["Day", "Month", "Year"];

    public bool IsEmpty => !IsLoading && Plans.Count == 0;

    public PlansViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    partial void OnPlansChanged(ObservableCollection<MembershipTypeDto> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Plans = new ObservableCollection<MembershipTypeDto>(
                await _mediator.Send(new GetAllPlansQuery(_session.CompanyId), ct));
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void OpenForm()
    {
        ErrorMessage = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new CreatePlanCommand(
                _session.CompanyId, Name, Basis, Price, DurationValue, DurationUnit));

            Name = string.Empty;
            Price = 0;
            DurationValue = 1;
            IsFormOpen = false;
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(MembershipTypeDto? plan)
    {
        if (plan is null) return;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new SetPlanActiveCommand(plan.Id, !plan.IsActive));
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
