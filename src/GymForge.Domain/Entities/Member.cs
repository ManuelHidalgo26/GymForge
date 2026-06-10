using GymForge.Domain.Enums;
using GymForge.Domain.Events;

namespace GymForge.Domain.Entities;

public class Member : BaseEntity
{
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }

    // Identity
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DocumentType DocumentType { get; private set; }
    public string DocumentNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Mobile { get; private set; }
    public DateOnly? BirthDate { get; private set; }
    public Gender Gender { get; private set; }

    // Media
    public string? PhotoUrl { get; private set; }
    public string? SignatureUrl { get; private set; }

    // Hardware credentials
    public string? TagSerial { get; private set; }
    public byte[]? FingerprintTemplate { get; private set; }

    // Emergency & Medical
    public string? EmergencyName { get; private set; }
    public string? EmergencyPhone { get; private set; }
    public string? EmergencyRelation { get; private set; }
    public string? MedicalConditions { get; private set; }
    public string? Medications { get; private set; }
    public string? Allergies { get; private set; }
    public BloodType BloodType { get; private set; } = BloodType.Unknown;
    public DateTime? WaiverSignedAt { get; private set; }
    public Guid? ParQId { get; private set; }

    // CRM
    public Guid? ReferredByMemberId { get; private set; }
    public MemberSource Source { get; private set; }
    public Guid? SalesRepId { get; private set; }
    public MemberStatus Status { get; private set; } = MemberStatus.Prospect;
    public DateOnly? JoinDate { get; private set; }
    public string? CustomFieldsJson { get; private set; }
    public bool MarketingConsent { get; private set; }
    public string? Observations { get; private set; }

    // Navigation
    public Site Site { get; private set; } = null!;
    public ICollection<Membership> Memberships { get; private set; } = [];
    public ICollection<Charge> Charges { get; private set; } = [];
    public ICollection<AccessLog> AccessLogs { get; private set; } = [];

    private Member() { }

    public static Member Create(
        Guid companyId,
        Guid siteId,
        string firstName,
        string lastName,
        DocumentType documentType,
        string documentNumber,
        Gender gender)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentNumber);

        var member = new Member
        {
            CompanyId = companyId,
            SiteId = siteId,
            FirstName = firstName,
            LastName = lastName,
            DocumentType = documentType,
            DocumentNumber = documentNumber,
            Gender = gender
        };

        member.AddDomainEvent(new MemberCreatedEvent(member.Id, companyId, siteId));
        return member;
    }

    public string FullName => $"{FirstName} {LastName}";
    public int? Age => BirthDate.HasValue
        ? (int)((DateTime.Today - BirthDate.Value.ToDateTime(TimeOnly.MinValue)).TotalDays / 365.25)
        : null;

    public void UpdateContact(string? email, string? mobile)
    {
        Email = email;
        Mobile = mobile;
        UpdatedAt = DateTime.UtcNow;
    }

    public void CompleteProfile(DateOnly? birthDate, MemberSource source, Guid? salesRepId, bool marketingConsent)
    {
        BirthDate = birthDate;
        Source = source;
        SalesRepId = salesRepId;
        MarketingConsent = marketingConsent;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePhoto(string photoUrl)
    {
        PhotoUrl = photoUrl;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFingerprintTemplate(byte[] template)
    {
        FingerprintTemplate = template;
        UpdatedAt = DateTime.UtcNow;
    }

    public void EnrollTag(string tagSerial)
    {
        TagSerial = tagSerial;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate(DateOnly joinDate)
    {
        Status = MemberStatus.Active;
        // Se conserva la fecha de alta original si el socio ya había ingresado antes.
        JoinDate ??= joinDate;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MemberActivatedEvent(Id, joinDate));
    }

    public void Freeze()
    {
        if (Status != MemberStatus.Active)
            throw new InvalidOperationException("Solo se puede congelar un socio activo.");
        Status = MemberStatus.Frozen;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unfreeze()
    {
        if (Status != MemberStatus.Frozen)
            throw new InvalidOperationException("El socio no está congelado.");
        Status = MemberStatus.Active;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkOverdue()
    {
        if (Status == MemberStatus.Active || Status == MemberStatus.Overdue)
        {
            Status = MemberStatus.Overdue;
            UpdatedAt = DateTime.UtcNow;
        }
    }

    public void Suspend()
    {
        Status = MemberStatus.Suspended;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel()
    {
        Status = MemberStatus.Cancelled;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MemberCancelledEvent(Id));
    }

    public void SignWaiver()
    {
        WaiverSignedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEmergencyContact(string name, string phone, string relation)
    {
        EmergencyName = name;
        EmergencyPhone = phone;
        EmergencyRelation = relation;
        UpdatedAt = DateTime.UtcNow;
    }
}
