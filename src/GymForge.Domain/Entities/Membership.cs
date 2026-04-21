using GymForge.Domain.Enums;
using GymForge.Domain.Events;

namespace GymForge.Domain.Entities;

public class Membership : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid MembershipTypeId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }

    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public MembershipStatus Status { get; private set; } = MembershipStatus.PendingActivation;

    // Freeze
    public DateOnly? FreezeStart { get; private set; }
    public DateOnly? FreezeEnd { get; private set; }
    public string? FreezeReason { get; private set; }
    public int FreezeCountUsed { get; private set; }

    // Billing
    public DateOnly? NextBillingDate { get; private set; }
    public bool AutoRenew { get; private set; } = true;
    public string? ContractPdfUrl { get; private set; }
    public DateTime? SignedAt { get; private set; }
    public Guid? DiscountId { get; private set; }

    // Pack tracking
    public int? VisitsRemaining { get; private set; }

    // Sales
    public Guid? SoldByStaffId { get; private set; }
    public decimal CommissionAmount { get; private set; }

    // Cancellation
    public DateTime? CancelDate { get; private set; }
    public string? CancelReason { get; private set; }

    // Navigation
    public Member Member { get; private set; } = null!;
    public MembershipType MembershipType { get; private set; } = null!;

    private Membership() { }

    public static Membership Create(
        Guid companyId,
        Guid siteId,
        Guid memberId,
        Guid membershipTypeId,
        DateOnly startDate,
        DateOnly? endDate,
        int? visitsRemaining = null,
        Guid? soldByStaffId = null)
    {
        return new Membership
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            MembershipTypeId = membershipTypeId,
            StartDate = startDate,
            EndDate = endDate,
            VisitsRemaining = visitsRemaining,
            SoldByStaffId = soldByStaffId,
            Status = MembershipStatus.PendingActivation
        };
    }

    public void Activate()
    {
        if (Status != MembershipStatus.PendingActivation && Status != MembershipStatus.Trial)
            throw new InvalidOperationException($"No se puede activar una membresía en estado {Status}.");

        Status = MembershipStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MembershipActivatedEvent(Id, MemberId));
    }

    public void Freeze(DateOnly freezeStart, DateOnly freezeEnd, string reason, int maxFreezeDaysPerYear = 90)
    {
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException("Solo se pueden congelar membresías activas.");

        int requestedDays = freezeEnd.DayNumber - freezeStart.DayNumber;
        if (requestedDays <= 0)
            throw new ArgumentException("La fecha de fin del congelamiento debe ser posterior al inicio.");

        Status = MembershipStatus.Frozen;
        FreezeStart = freezeStart;
        FreezeEnd = freezeEnd;
        FreezeReason = reason;
        FreezeCountUsed += requestedDays;

        if (EndDate.HasValue)
            EndDate = EndDate.Value.AddDays(requestedDays);

        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MembershipFrozenEvent(Id, MemberId, freezeStart, freezeEnd));
    }

    public void Unfreeze()
    {
        if (Status != MembershipStatus.Frozen)
            throw new InvalidOperationException("La membresía no está congelada.");

        Status = MembershipStatus.Active;
        FreezeStart = null;
        FreezeEnd = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel(string reason)
    {
        Status = MembershipStatus.Cancelled;
        CancelDate = DateTime.UtcNow;
        CancelReason = reason;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MembershipCancelledEvent(Id, MemberId, reason));
    }

    public void Expire()
    {
        Status = MembershipStatus.Expired;
        UpdatedAt = DateTime.UtcNow;
    }

    public void DecrementVisit()
    {
        if (VisitsRemaining is null) return;
        if (VisitsRemaining <= 0)
            throw new InvalidOperationException("No quedan visitas en el pack.");
        VisitsRemaining--;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsExpired(DateOnly today) =>
        EndDate.HasValue && EndDate.Value < today;

    public bool IsActive => Status == MembershipStatus.Active;
}
