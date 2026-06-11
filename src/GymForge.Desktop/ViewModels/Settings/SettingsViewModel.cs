using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Settings;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>Configuración: datos del gimnasio, sedes (ABM) y reglas de acceso.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ISiteRepository _siteRepo;
    private readonly SessionContext _session;
    private readonly GatekeeperConfig _gatekeeper;
    private Guid? _editingSiteId;   // null = agregando

    // Datos del gimnasio
    [ObservableProperty] private string _legalName = string.Empty;
    [ObservableProperty] private string _taxId = string.Empty;
    [ObservableProperty] private string? _companyMessage;
    [ObservableProperty] private bool _companySaved;

    // Sedes
    [ObservableProperty] private ObservableCollection<SiteDto> _sites = [];
    [ObservableProperty] private string _siteFormTitle = "Agregar sede";
    [ObservableProperty] private string _newSiteName = string.Empty;
    [ObservableProperty] private string _newSiteAddress = string.Empty;
    [ObservableProperty] private string _newSitePhone = string.Empty;
    [ObservableProperty] private string? _siteMessage;
    [ObservableProperty] private bool _siteSaved;
    [ObservableProperty] private bool _isEditingSite;

    // Reglas de acceso
    [ObservableProperty] private decimal _stopOnOweAmount;
    [ObservableProperty] private decimal _warnOnOweAmount;
    [ObservableProperty] private int _antiPassbackMinutes;
    [ObservableProperty] private string? _accessMessage;
    [ObservableProperty] private bool _accessSaved;

    public SettingsViewModel(
        IMediator mediator, ISiteRepository siteRepo, SessionContext session, GatekeeperConfig gatekeeper)
    {
        _mediator = mediator;
        _siteRepo = siteRepo;
        _session = session;
        _gatekeeper = gatekeeper;
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var company = await _siteRepo.GetCompanyAsync(_session.CompanyId, ct);
        if (company is not null)
        {
            LegalName = company.LegalName;
            TaxId = company.TaxId;
        }

        var sites = await _siteRepo.GetByCompanyAsync(_session.CompanyId, ct);
        Sites = new ObservableCollection<SiteDto>(
            sites.Select(s => new SiteDto(s.Id, s.Name, s.Address, s.Phone)));

        StopOnOweAmount = _gatekeeper.StopOnOweAmount;
        WarnOnOweAmount = _gatekeeper.WarnOnOweAmount;
        AntiPassbackMinutes = _gatekeeper.AntiPassbackMinutes;
    }

    // ── Datos del gimnasio ──────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        CompanyMessage = null;
        CompanySaved = false;
        try
        {
            await _mediator.Send(new UpdateCompanyCommand(_session.CompanyId, LegalName, TaxId));
            await _session.InitializeAsync();
            CompanySaved = true;
            CompanyMessage = "Datos guardados.";
        }
        catch (ValidationException vex)
        {
            CompanyMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { CompanyMessage = ex.Message; }
    }

    // ── Sedes ───────────────────────────────────────────────────────────────

    [RelayCommand]
    private void EditSite(SiteDto? site)
    {
        if (site is null) return;
        _editingSiteId = site.Id;
        IsEditingSite = true;
        SiteFormTitle = $"Modificar: {site.Name}";
        NewSiteName = site.Name;
        NewSiteAddress = site.Address;
        NewSitePhone = site.Phone ?? string.Empty;
        SiteMessage = null;
    }

    [RelayCommand]
    private void CancelSiteEdit()
    {
        _editingSiteId = null;
        IsEditingSite = false;
        SiteFormTitle = "Agregar sede";
        NewSiteName = string.Empty;
        NewSiteAddress = string.Empty;
        NewSitePhone = string.Empty;
        SiteMessage = null;
    }

    [RelayCommand]
    private async Task SaveSiteAsync()
    {
        SiteMessage = null;
        SiteSaved = false;
        try
        {
            var phone = string.IsNullOrWhiteSpace(NewSitePhone) ? null : NewSitePhone;
            string message;

            if (_editingSiteId is { } id)
            {
                await _mediator.Send(new UpdateSiteCommand(id, NewSiteName, NewSiteAddress, phone));
                message = "Sede actualizada.";
            }
            else
            {
                await _mediator.Send(new CreateSiteCommand(_session.CompanyId, NewSiteName, NewSiteAddress));
                message = "Sede agregada.";
            }

            CancelSiteEdit();
            SiteSaved = true;
            SiteMessage = message;
            await _session.InitializeAsync();   // refresca el selector del topbar
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            SiteMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { SiteMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteSiteAsync(SiteDto? site)
    {
        if (site is null) return;
        SiteMessage = null;
        SiteSaved = false;
        try
        {
            var deleted = await _mediator.Send(new DeleteSiteCommand(site.Id));
            SiteSaved = true;
            SiteMessage = deleted
                ? "Sede eliminada."
                : "La sede tenía socios o movimientos: se desactivó y ya no aparece (el historial se conserva).";
            await _session.InitializeAsync();
            await LoadAsync();
        }
        catch (Exception ex) { SiteMessage = ex.Message; }
    }

    // ── Reglas de acceso ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAccessAsync()
    {
        AccessMessage = null;
        AccessSaved = false;
        try
        {
            await _mediator.Send(new UpdateAccessSettingsCommand(
                _session.CompanyId, StopOnOweAmount, WarnOnOweAmount, AntiPassbackMinutes));
            AccessSaved = true;
            AccessMessage = "Reglas de acceso guardadas.";
        }
        catch (ValidationException vex)
        {
            AccessMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { AccessMessage = ex.Message; }
    }
}
