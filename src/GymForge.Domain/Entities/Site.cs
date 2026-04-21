namespace GymForge.Domain.Entities;

public class Site : BaseEntity
{
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public string? Phone { get; private set; }
    public Guid? ManagerStaffId { get; private set; }
    public string? OpenHoursJson { get; private set; }
    public double? GeoLat { get; private set; }
    public double? GeoLng { get; private set; }
    public string BrandColorHex { get; private set; } = "#6366F1";
    public bool IsActive { get; private set; } = true;

    public Company Company { get; private set; } = null!;
    public ICollection<Member> Members { get; private set; } = [];

    private Site() { }

    public static Site Create(Guid companyId, string name, string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        return new Site { CompanyId = companyId, Name = name, Address = address };
    }

    public void Update(string name, string address, string? phone)
    {
        Name = name;
        Address = address;
        Phone = phone;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }
}
