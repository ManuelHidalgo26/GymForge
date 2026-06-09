using CommunityToolkit.Mvvm.ComponentModel;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GymForge.Desktop.Services;

public sealed record SiteOption(Guid Id, string Name);

/// <summary>
/// Estado de sesión vivo de la app: tenant + sede activa + cajero + turno de caja.
/// Reemplaza los GUID de company/site que estaban hardcodeados en las ViewModels.
/// Singleton; se inicializa una vez en el arranque tras migrate+seed.
/// </summary>
public partial class SessionContext : ObservableObject
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SessionContext(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    public Guid CompanyId { get; private set; }

    [ObservableProperty] private string _gymName = "GymForge";

    public IReadOnlyList<SiteOption> Sites { get; private set; } = [];

    [ObservableProperty] private SiteOption? _currentSite;

    public Guid SiteId => CurrentSite?.Id ?? Guid.Empty;

    // ── Cajero ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn))]
    private Guid? _cashierId;

    [ObservableProperty] private string? _cashierName;

    public bool IsSignedIn => CashierId.HasValue;

    // ── Turno de caja ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasOpenShift))]
    private Guid? _openShiftId;

    public bool HasOpenShift => OpenShiftId.HasValue;

    /// <summary>Carga el tenant por defecto (primera company) y sus sedes desde la DB.</summary>
    public async Task InitializeAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var sites = scope.ServiceProvider.GetRequiredService<ISiteRepository>();

        var companies = await sites.GetCompaniesAsync();
        var company = companies.FirstOrDefault();
        if (company is null) return;

        CompanyId = company.Id;
        GymName = company.LegalName;

        var siteList = await sites.GetByCompanyAsync(company.Id);
        Sites = siteList.Select(s => new SiteOption(s.Id, s.Name)).ToList();
        CurrentSite = Sites.FirstOrDefault();
    }

    public void SignIn(StaffDto staff)
    {
        CashierId = staff.Id;
        CashierName = staff.FullName;
    }

    public void SignOut()
    {
        CashierId = null;
        CashierName = null;
        OpenShiftId = null;
    }

    public void SetOpenShift(Guid? shiftId) => OpenShiftId = shiftId;

    partial void OnCurrentSiteChanged(SiteOption? value)
    {
        // Cambiar de sede cierra la referencia al turno (es por sede).
        OpenShiftId = null;
        OnPropertyChanged(nameof(SiteId));
    }
}
