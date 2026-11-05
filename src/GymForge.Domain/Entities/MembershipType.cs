using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class MembershipType : BaseEntity, ITenantScoped, IAuditable
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

    /// <summary>Vencimiento = inicio + duración del plan. Una unidad desconocida cuenta como meses.</summary>
    public DateOnly? CalculateEndDate(DateOnly start) => DurationUnit switch
    {
        "Day" => start.AddDays(DurationValue),
        "Year" => start.AddYears(DurationValue),
        _ => start.AddMonths(DurationValue),
    };

    /// <summary>Hora de pared (ya en la zona de la empresa) permitida por las restricciones horarias del plan.</summary>
    public bool IsTimeAllowed(DateTime localTime)
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
    public void Activate() { IsActive = true; UpdatedAt = DateTime.UtcNow; }

    private record ScheduleRestriction(int[] Days, TimeOnly From, TimeOnly To);
}
