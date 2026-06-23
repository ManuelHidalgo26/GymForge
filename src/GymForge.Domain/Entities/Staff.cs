using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Staff : BaseEntity
{
    public Guid CompanyId { get; private set; }
    public string SiteIdsJson { get; private set; } = "[]";
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Mobile { get; private set; }
    public StaffRole Role { get; private set; }
    public string SpecialtiesJson { get; private set; } = "[]";
    public string CertificationsJson { get; private set; } = "[]";
    public string PermissionsJson { get; private set; } = "{}";
    public string PinCodeHash { get; private set; } = string.Empty;
    public string ColorHex { get; private set; } = "#6366F1";
    public decimal CommissionPctMemberships { get; private set; }
    public decimal CommissionPctPt { get; private set; }
    public decimal CommissionPctProducts { get; private set; }
    public bool IsActive { get; private set; } = true;
    public string? AvatarUrl { get; private set; }

    public Company Company { get; private set; } = null!;

    private Staff() { }

    public static Staff Create(
        Guid companyId,
        string firstName,
        string lastName,
        StaffRole role,
        string pinCodeHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pinCodeHash);

        return new Staff
        {
            CompanyId = companyId,
            FirstName = firstName,
            LastName = lastName,
            Role = role,
            PinCodeHash = pinCodeHash
        };
    }

    public string FullName => $"{FirstName} {LastName}";

    public void SetPermissions(string permissionsJson)
    {
        PermissionsJson = permissionsJson;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Reemplaza el hash del PIN (login del cajero). El hashing vive en la capa de aplicación.</summary>
    public void ChangePin(string newPinCodeHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPinCodeHash);
        PinCodeHash = newPinCodeHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}
