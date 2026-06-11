using CommunityToolkit.Mvvm.ComponentModel;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Settings;
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
    private readonly GatekeeperConfig _gatekeeper;

    public SessionContext(IServiceScopeFactory scopeFactory, GatekeeperConfig gatekeeper)
    {
        _scopeFactory = scopeFactory;
        _gatekeeper = gatekeeper;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Staff por defecto (admin) para operaciones sin login explícito de caja.</summary>
    public Guid DefaultStaffId { get; private set; }

    /// <summary>Cajero efectivo: el logueado o, si no hay, el staff por defecto.</summary>
    public Guid EffectiveCashierId => CashierId ?? DefaultStaffId;

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
        var staffRepo = scope.ServiceProvider.GetRequiredService<IStaffRepository>();

        var companies = await sites.GetCompaniesAsync();
        var company = companies.FirstOrDefault();
        if (company is null) return;

        CompanyId = company.Id;
        GymName = company.LegalName;

        // Reglas de acceso persistidas → gatekeeper en memoria.
        if (AccessSettings.FromJson(company.SettingsJson) is { } access)
        {
            _gatekeeper.StopOnOweAmount = access.StopOnOweAmount;
            _gatekeeper.WarnOnOweAmount = access.WarnOnOweAmount;
            _gatekeeper.AntiPassbackMinutes = access.AntiPassbackMinutes;
        }

        var siteList = await sites.GetByCompanyAsync(company.Id);
        Sites = siteList.Select(s => new SiteOption(s.Id, s.Name)).ToList();
        CurrentSite = Sites.FirstOrDefault();

        var staff = await staffRepo.GetActiveByCompanyAsync(company.Id);
        DefaultStaffId = staff.FirstOrDefault()?.Id ?? Guid.Empty;
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
