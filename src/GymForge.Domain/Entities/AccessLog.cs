using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

/// <summary>Append-only. Never update or delete rows.</summary>
public class AccessLog : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public int DoorId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTime SwipedAt { get; private set; }
    public AccessMethod Method { get; private set; }
    public string? TagSerial { get; private set; }
    public bool AccessGranted { get; private set; }
    public AccessDenialReason? DenialReason { get; private set; }
    public AccessDirection Direction { get; private set; }

    // Navigation (no delete cascade — access logs outlive members)
    public Member Member { get; private set; } = null!;

    private AccessLog() { }

    public static AccessLog Granted(
        Guid companyId,
        Guid siteId,
        Guid memberId,
        Guid? membershipId,
        int doorId,
        AccessMethod method,
        AccessDirection direction,
        string? tagSerial = null)
    {
        return new AccessLog
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            MembershipId = membershipId,
            DoorId = doorId,
            Method = method,
            Direction = direction,
            TagSerial = tagSerial,
            AccessGranted = true,
            SwipedAt = DateTime.UtcNow
        };
    }

    public static AccessLog Denied(
        Guid companyId,
        Guid siteId,
        Guid memberId,
        int doorId,
        AccessMethod method,
        AccessDenialReason reason,
        string? tagSerial = null)
    {
        return new AccessLog
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            DoorId = doorId,
            Method = method,
            Direction = AccessDirection.In,
            TagSerial = tagSerial,
            AccessGranted = false,
            DenialReason = reason,
            SwipedAt = DateTime.UtcNow
        };
    }
}
