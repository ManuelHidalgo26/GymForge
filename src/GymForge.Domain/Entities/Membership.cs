using GymForge.Domain.Enums;
using GymForge.Domain.Events;

namespace GymForge.Domain.Entities;

public class Membership : BaseEntity, ISiteScoped, IAuditable
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
    /// <summary>Acumulado histórico de días congelados (informativo; no se reinicia).</summary>
    public int FreezeCountUsed { get; private set; }
    /// <summary>Días congelados en el año calendario <see cref="FreezeYear"/>; es lo que topea el cupo anual.</summary>
    public int FreezeDaysUsedYear { get; private set; }
    /// <summary>Año calendario del cupo en curso (0 = nunca se congeló).</summary>
    public int FreezeYear { get; private set; }

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

    /// <summary>Congela la comisión de la venta: quién vendió y cuánto le toca (no se recalcula si cambia el %).</summary>
    public void SetCommission(Guid staffId, decimal amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        SoldByStaffId = staffId;
        CommissionAmount = amount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (Status != MembershipStatus.PendingActivation && Status != MembershipStatus.Trial)
            throw new InvalidOperationException($"No se puede activar una membresía en estado {Status}.");

        Status = MembershipStatus.Active;
        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MembershipActivatedEvent(Id, MemberId));
    }

    /// <summary>Días de congelamiento ya usados en el año calendario <paramref name="year"/> (el cupo se reinicia cada año).</summary>
    public int FreezeDaysUsedIn(int year) => FreezeYear == year ? FreezeDaysUsedYear : 0;

    /// <summary>
    /// Congela la membresía y extiende el vencimiento por los días pedidos. El cupo es anual
    /// (año calendario de <paramref name="freezeStart"/>) y se acumula por membresía.
    /// </summary>
    public void Freeze(
        DateOnly freezeStart, DateOnly freezeEnd, string reason,
        int maxFreezeDaysPerYear = Company.DefaultMaxFreezeDaysPerYear)
    {
        if (Status != MembershipStatus.Active)
            throw new InvalidOperationException("Solo se pueden congelar membresías activas.");

        int requestedDays = freezeEnd.DayNumber - freezeStart.DayNumber;
        if (requestedDays <= 0)
            throw new ArgumentException("La fecha de fin del congelamiento debe ser posterior al inicio.");

        int year = freezeStart.Year;
        int usedThisYear = FreezeDaysUsedIn(year);
        if (usedThisYear + requestedDays > maxFreezeDaysPerYear)
            throw new InvalidOperationException(
                $"Supera el tope de {maxFreezeDaysPerYear} días de congelamiento por año " +
                $"(usados {usedThisYear}, solicitados {requestedDays}).");

        Status = MembershipStatus.Frozen;
        FreezeStart = freezeStart;
        FreezeEnd = freezeEnd;
        FreezeReason = reason;
        FreezeYear = year;
        FreezeDaysUsedYear = usedThisYear + requestedDays;
        FreezeCountUsed += requestedDays;

        if (EndDate.HasValue)
            EndDate = EndDate.Value.AddDays(requestedDays);

        UpdatedAt = DateTime.UtcNow;
        AddDomainEvent(new MembershipFrozenEvent(Id, MemberId, freezeStart, freezeEnd));
    }

    /// <summary>
    /// Reactiva la membresía. Si se descongela antes de <see cref="FreezeEnd"/>, deshace la extensión
    /// del vencimiento por los días que no se consumieron y devuelve ese cupo.
    /// </summary>
    public void Unfreeze(DateOnly today)
    {
        if (Status != MembershipStatus.Frozen)
            throw new InvalidOperationException("La membresía no está congelada.");

        if (FreezeStart is { } start && FreezeEnd is { } end && today < end)
        {
            // Si el freeze todavía no empezó no se consumió nada: se devuelve el período completo.
            var consumedUntil = today > start ? today : start;
            ReleaseUnusedFreezeDays(consumedUntil, end);
        }

        Status = MembershipStatus.Active;
        FreezeStart = null;
        FreezeEnd = null;
        FreezeReason = null;
        UpdatedAt = DateTime.UtcNow;
    }

    private void ReleaseUnusedFreezeDays(DateOnly consumedUntil, DateOnly freezeEnd)
    {
        int unusedDays = freezeEnd.DayNumber - consumedUntil.DayNumber;
        if (EndDate.HasValue)
            EndDate = EndDate.Value.AddDays(-unusedDays);

        FreezeDaysUsedYear = Math.Max(0, FreezeDaysUsedYear - unusedDays);
        FreezeCountUsed = Math.Max(0, FreezeCountUsed - unusedDays);
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
