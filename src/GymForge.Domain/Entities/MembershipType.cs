using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class MembershipType : BaseEntity
{
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public MembershipBasis Basis { get; private set; }
    public int DurationValue { get; private set; }
    public string DurationUnit { get; private set; } = "Month";
    public decimal Price { get; private set; }
    public decimal SignupFee { get; private set; }
    public string BillingCycle { get; private set; } = "Monthly";
    public string AllowedDoorIdsJson { get; private set; } = "[]";
    public string AllowedClassTypesJson { get; private set; } = "[]";
    public int? ClassCreditsIncluded { get; private set; }
    public int PtSessionsIncluded { get; private set; }
    public string? ScheduleRestrictionJson { get; private set; }
    public int? AgeMin { get; private set; }
    public int? AgeMax { get; private set; }
    public Gender? GenderRestriction { get; private set; }
    public string BenefitIdsJson { get; private set; } = "[]";
    public bool IsActive { get; private set; } = true;
    public string? Description { get; private set; }
    public string ColorHex { get; private set; } = "#6366F1";

    private List<int>? _allowedDoorIdsCache;

    public IReadOnlyList<int> AllowedDoorIds
    {
        get
        {
            _allowedDoorIdsCache ??= System.Text.Json.JsonSerializer.Deserialize<List<int>>(AllowedDoorIdsJson) ?? [];
            return _allowedDoorIdsCache;
        }
    }

    private MembershipType() { }

    public static MembershipType Create(
        Guid companyId,
        string name,
        MembershipBasis basis,
        decimal price,
        int durationValue = 1,
        string durationUnit = "Month")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (price < 0) throw new ArgumentException("Price cannot be negative.");

        return new MembershipType
        {
            CompanyId = companyId,
            Name = name,
            Basis = basis,
            Price = price,
            DurationValue = durationValue,
            DurationUnit = durationUnit
        };
    }

    public bool IsTimeAllowed(DateTime localTime, string timezone)
    {
        if (string.IsNullOrEmpty(ScheduleRestrictionJson)) return true;

        var restrictions = System.Text.Json.JsonSerializer.Deserialize<ScheduleRestriction[]>(ScheduleRestrictionJson);
        if (restrictions is null || restrictions.Length == 0) return true;

        var dayOfWeek = (int)localTime.DayOfWeek;
        var timeOfDay = TimeOnly.FromDateTime(localTime);

        foreach (var r in restrictions)
        {
            if (r.Days.Contains(dayOfWeek) &&
                timeOfDay >= r.From &&
                timeOfDay <= r.To)
                return true;
        }

        return false;
    }

    public void Deactivate() { IsActive = false; UpdatedAt = DateTime.UtcNow; }

    private record ScheduleRestriction(int[] Days, TimeOnly From, TimeOnly To);
}
