namespace GymForge.Domain.Entities;

public class Company : BaseEntity
{
    public string LegalName { get; private set; } = string.Empty;
    public string TaxId { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string PrimaryLanguage { get; private set; } = "es-AR";
    public string Currency { get; private set; } = "ARS";
    public string Timezone { get; private set; } = "Argentina Standard Time";
    public string? FiscalConfigJson { get; private set; }
    public string BrandColorHex { get; private set; } = "#6366F1";
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

    public void SetFiscalConfig(string fiscalConfigJson)
    {
        FiscalConfigJson = fiscalConfigJson;
        UpdatedAt = DateTime.UtcNow;
    }
}
