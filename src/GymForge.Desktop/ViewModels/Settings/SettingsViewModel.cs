using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Settings;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Settings;

/// <summary>Configuración: datos del gimnasio (nombre/CUIT) y sedes.</summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly ISiteRepository _siteRepo;
    private readonly SessionContext _session;

    // Datos del gimnasio
    [ObservableProperty] private string _legalName = string.Empty;
    [ObservableProperty] private string _taxId = string.Empty;
    [ObservableProperty] private string? _companyMessage;
    [ObservableProperty] private bool _companySaved;

    // Sedes
    [ObservableProperty] private ObservableCollection<SiteDto> _sites = [];
    [ObservableProperty] private string _newSiteName = string.Empty;
    [ObservableProperty] private string _newSiteAddress = string.Empty;
    [ObservableProperty] private string? _siteMessage;

    public SettingsViewModel(IMediator mediator, ISiteRepository siteRepo, SessionContext session)
    {
        _mediator = mediator;
        _siteRepo = siteRepo;
        _session = session;
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
        Sites = new ObservableCollection<SiteDto>(sites.Select(s => new SiteDto(s.Id, s.Name, s.Address)));
    }

    [RelayCommand]
    private async Task SaveCompanyAsync()
    {
        CompanyMessage = null;
        CompanySaved = false;
        try
        {
            await _mediator.Send(new UpdateCompanyCommand(_session.CompanyId, LegalName, TaxId));
            await _session.InitializeAsync();   // refresca el nombre en sidebar/título
            CompanySaved = true;
            CompanyMessage = "Datos guardados.";
        }
        catch (ValidationException vex)
        {
            CompanyMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { CompanyMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task AddSiteAsync()
    {
        SiteMessage = null;
        try
        {
            await _mediator.Send(new CreateSiteCommand(_session.CompanyId, NewSiteName, NewSiteAddress));
            NewSiteName = string.Empty;
            NewSiteAddress = string.Empty;
            await _session.InitializeAsync();   // el selector de sede del topbar incluye la nueva
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            SiteMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { SiteMessage = ex.Message; }
    }
}
