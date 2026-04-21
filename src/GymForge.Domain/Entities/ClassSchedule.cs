using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class ClassDescription : BaseEntity
{
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Category { get; private set; }
    public string? Description { get; private set; }
    public string? ImageUrl { get; private set; }
    public int DefaultDurationMin { get; private set; } = 60;
    public int DefaultCapacity { get; private set; } = 20;
    public string? EquipmentNeeded { get; private set; }
    public bool IsActive { get; private set; } = true;

    public ICollection<ClassSchedule> Schedules { get; private set; } = [];

    private ClassDescription() { }

    public static ClassDescription Create(Guid companyId, string name, int durationMin = 60, int capacity = 20) =>
        new() { CompanyId = companyId, Name = name, DefaultDurationMin = durationMin, DefaultCapacity = capacity };
}

public class ClassSchedule : BaseEntity
{
    public Guid ClassDescriptionId { get; private set; }
    public Guid? InstructorId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string? RoomId { get; private set; }

    public DateTime StartDatetime { get; private set; }
    public DateTime EndDatetime { get; private set; }
    public int Capacity { get; private set; }
    public int WaitlistCapacity { get; private set; } = 5;
    public bool IsRecurring { get; private set; }
    public string? RecurrenceRule { get; private set; }

    public DateTime? BookingOpenFrom { get; private set; }
    public int BookingCloseBeforeMin { get; private set; } = 30;
    public int CancelDeadlineMin { get; private set; } = 120;
    public decimal? LateCancelFee { get; private set; }
    public decimal? NoShowFee { get; private set; }
    public int? MinAge { get; private set; }
    public decimal? DropInPrice { get; private set; }
    public bool IsCancelled { get; private set; }

    public ClassDescription ClassDescription { get; private set; } = null!;
    public ICollection<Booking> Bookings { get; private set; } = [];

    public int AttendedCount => Bookings.Count(b => b.Status == BookingStatus.Attended);
    public int BookedCount => Bookings.Count(b => b.Status == BookingStatus.Booked);
    public int WaitlistCount => Bookings.Count(b => b.Status == BookingStatus.Waitlisted);
    public bool IsFull => BookedCount >= Capacity;

    private ClassSchedule() { }

    public static ClassSchedule Create(
        Guid companyId,
        Guid siteId,
        Guid classDescriptionId,
        DateTime start,
        DateTime end,
        int capacity) =>
        new()
        {
            CompanyId = companyId,
            SiteId = siteId,
            ClassDescriptionId = classDescriptionId,
            StartDatetime = start,
            EndDatetime = end,
            Capacity = capacity
        };
}

public class Booking : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid ClassScheduleId { get; private set; }
    public Guid CompanyId { get; private set; }
    public BookingType BookingType { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.Booked;
    public int? WaitlistPosition { get; private set; }
    public DateTime BookedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; private set; }
    public DateTime? CheckedInAt { get; private set; }
    public BookingChannel BookingChannel { get; private set; }
    public Guid? PaymentChargeId { get; private set; }

    public Member Member { get; private set; } = null!;
    public ClassSchedule ClassSchedule { get; private set; } = null!;

    private Booking() { }

    public static Booking Create(
        Guid companyId,
        Guid memberId,
        Guid classScheduleId,
        BookingChannel channel = BookingChannel.FrontDesk) =>
        new()
        {
            CompanyId = companyId,
            MemberId = memberId,
            ClassScheduleId = classScheduleId,
            BookingChannel = channel,
            Status = BookingStatus.Booked,
            BookedAt = DateTime.UtcNow
        };

    public static Booking CreateWaitlisted(
        Guid companyId,
        Guid memberId,
        Guid classScheduleId,
        int position) =>
        new()
        {
            CompanyId = companyId,
            MemberId = memberId,
            ClassScheduleId = classScheduleId,
            Status = BookingStatus.Waitlisted,
            WaitlistPosition = position,
            BookedAt = DateTime.UtcNow
        };

    public void CheckIn()
    {
        if (Status != BookingStatus.Booked)
            throw new InvalidOperationException("Solo se puede hacer check-in de reservas confirmadas.");
        Status = BookingStatus.Attended;
        CheckedInAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Cancel(bool isLate = false)
    {
        Status = isLate ? BookingStatus.LateCancelled : BookingStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkNoShow()
    {
        Status = BookingStatus.NoShow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void PromoteFromWaitlist()
    {
        Status = BookingStatus.Booked;
        WaitlistPosition = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
