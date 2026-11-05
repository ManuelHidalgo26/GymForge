using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>
/// Card "Comisiones del personal" de Configuración: lista el personal activo con sus porcentajes (0 a 100)
/// de membresías y productos, editables y guardados por persona. Un cambio afecta solo a las ventas futuras:
/// las comisiones ya vendidas quedan congeladas.
/// </summary>
public partial class StaffCommissionsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;

    [ObservableProperty] private ObservableCollection<StaffCommissionEditor> _staff = [];

    public StaffCommissionsViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var rates = await _mediator.Send(new GetStaffCommissionRatesQuery(_session.CompanyId), ct);
        Staff = new ObservableCollection<StaffCommissionEditor>(rates.Select(r => new StaffCommissionEditor(r)));
    }

    [RelayCommand]
    private async Task SaveAsync(StaffCommissionEditor? row)
    {
        if (row is null) return;

        row.Message = null;
        row.Saved = false;
        try
        {
            var saved = await _mediator.Send(new UpdateStaffCommissionsCommand(
                _session.CompanyId, row.StaffId, row.PctMemberships ?? 0m, row.PctProducts ?? 0m));
            row.PctMemberships = saved.PctMemberships;
            row.PctProducts = saved.PctProducts;
            row.Saved = true;
            row.Message = "Guardado.";
        }
        catch (ValidationException vex)
        {
            row.Message = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { row.Message = ex.Message; }
    }
}

/// <summary>Una fila editable: el personal con sus porcentajes y el resultado de su último guardado.</summary>
public partial class StaffCommissionEditor : ObservableObject
{
    public Guid StaffId { get; }
    public string FullName { get; }

    [ObservableProperty] private decimal? _pctMemberships;
    [ObservableProperty] private decimal? _pctProducts;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _saved;

    public StaffCommissionEditor(StaffCommissionRatesDto rates)
    {
        StaffId = rates.StaffId;
        FullName = rates.FullName;
        _pctMemberships = rates.PctMemberships;
        _pctProducts = rates.PctProducts;
    }
}
