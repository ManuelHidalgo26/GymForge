using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Licensing;
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
    private readonly CurrentLicense _license;
    private readonly ILicenseService _licenseService;

    public SessionContext(
        IServiceScopeFactory scopeFactory, GatekeeperConfig gatekeeper,
        CurrentLicense license, ILicenseService licenseService)
    {
        _scopeFactory = scopeFactory;
        _gatekeeper = gatekeeper;
        _license = license;
        _licenseService = licenseService;
    }

    public Guid CompanyId { get; private set; }

    /// <summary>Staff por defecto (admin) para operaciones sin login explícito de caja.</summary>
    public Guid DefaultStaffId { get; private set; }

    /// <summary>Cajero efectivo: el logueado o, si no hay, el staff por defecto.</summary>
    public Guid EffectiveCashierId => CashierId ?? DefaultStaffId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GymInitials))]
    private string _gymName = "GymForge";

    // ── Marca (branding del gimnasio) ────────────────────────────────────────
    public const string DefaultBrandColorHex = "#6366F1";

    /// <summary>Color de acento persistido; re-tinta toda la UI vía FluentAvaloniaTheme.</summary>
    [ObservableProperty] private string _brandColorHex = DefaultBrandColorHex;

    /// <summary>Logo del gimnasio para el shell/recibos; null = usar iniciales.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private Bitmap? _logoImage;

    public bool HasLogo => LogoImage is not null;

    /// <summary>Ruta local del logo en disco (para el recibo PDF y re-carga).</summary>
    public string? LogoPath { get; private set; }

    /// <summary>Iniciales del nombre del gimnasio, para el fallback sin logo.</summary>
    public string GymInitials => BuildInitials(GymName);

    public IReadOnlyList<SiteOption> Sites { get; private set; } = [];

    [ObservableProperty] private SiteOption? _currentSite;

    public Guid SiteId => CurrentSite?.Id ?? Guid.Empty;

    // ── Cajero ──────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn))]
    private Guid? _cashierId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CashierInitials))]
    private string? _cashierName;

    public bool IsSignedIn => CashierId.HasValue;

    /// <summary>Iniciales del cajero para el avatar del topbar ("?" si no hay sesión).</summary>
    public string CashierInitials => string.IsNullOrWhiteSpace(CashierName) ? "?" : BuildInitials(CashierName);

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

        // Marca: color de acento + logo desde disco (si existe).
        BrandColorHex = string.IsNullOrWhiteSpace(company.BrandColorHex)
            ? DefaultBrandColorHex : company.BrandColorHex;
        LogoPath = string.IsNullOrWhiteSpace(company.LogoUrl) ? null : company.LogoUrl;
        LogoImage = TryLoadLogo(LogoPath);

        // Licencia persistida → estado/límites en memoria (Free si no hay clave).
        _license.State = _licenseService.Resolve(
            company.LicenseKey, DateOnly.FromDateTime(DateTime.Now));

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

    /// <summary>Carga un Bitmap desde un archivo sin dejarlo bloqueado (para poder
    /// reemplazar el logo más tarde). Devuelve null si no existe o no decodifica.</summary>
    private static Bitmap? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch { return null; }
    }

    /// <summary>Iniciales para el fallback sin logo: 1-2 letras del nombre del gimnasio.</summary>
    private static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "GF";
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = words[0];
        if (words.Length >= 2)
            return string.Concat(first[0], words[1][0]).ToUpperInvariant();
        return (first.Length >= 2 ? first[..2] : first).ToUpperInvariant();
    }

    partial void OnCurrentSiteChanged(SiteOption? value)
    {
        // Cambiar de sede cierra la referencia al turno (es por sede).
        OpenShiftId = null;
        OnPropertyChanged(nameof(SiteId));
    }
}
