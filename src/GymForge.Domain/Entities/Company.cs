namespace GymForge.Domain.Entities;

public class Company : BaseEntity
{
    public const int DefaultMaxFreezeDaysPerYear = 90;

    public string LegalName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string PrimaryLanguage { get; private set; } = "es-AR";
    public string Currency { get; private set; } = "ARS";
    public string Timezone { get; private set; } = "Argentina Standard Time";
    public string? FiscalConfigJson { get; private set; }
    /// <summary>Configuración general (JSON): reglas de acceso, etc.</summary>
    public string? SettingsJson { get; private set; }
    /// <summary>Clave de licencia firmada (null = tier Free).</summary>
    public string? LicenseKey { get; private set; }
    public string BrandColorHex { get; private set; } = "#6366F1";
    /// <summary>Tope de días de congelamiento de membresía por año calendario.</summary>
    public int MaxFreezeDaysPerYear { get; private set; } = DefaultMaxFreezeDaysPerYear;
    public bool IsActive { get; private set; } = true;

    public ICollection<Site> Sites { get; private set; } = [];
    public ICollection<Staff> Staff { get; private set; } = [];

    private Company() { }

    public static Company Create(string legalName, string taxId, string timezone = "Argentina Standard Time")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxId);

        return new Company
        {
            LegalName = legalName,
            TaxId = taxId,
            Timezone = timezone
        };
    }

    public void UpdateIdentity(string legalName, string taxId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(taxId);
        LegalName = legalName;
        TaxId = taxId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateBranding(string? logoUrl, string brandColorHex)
    {
        LogoUrl = logoUrl;
        BrandColorHex = brandColorHex;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetSettings(string settingsJson)
    {
        SettingsJson = settingsJson;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFiscalConfig(string fiscalConfigJson)
    {
        FiscalConfigJson = fiscalConfigJson;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFreezePolicy(int maxFreezeDaysPerYear)
    {
        if (maxFreezeDaysPerYear < 0)
            throw new ArgumentException("El tope no puede ser negativo.", nameof(maxFreezeDaysPerYear));

        MaxFreezeDaysPerYear = maxFreezeDaysPerYear;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ActivateLicense(string licenseKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);
        LicenseKey = licenseKey.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
