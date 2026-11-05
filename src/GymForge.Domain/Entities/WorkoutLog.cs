namespace GymForge.Domain.Entities;

public class WorkoutLog : BaseEntity, ISiteScoped
{
    public Guid MemberId { get; private set; }
    public Guid RoutineItemId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    public DateTime PerformedAt { get; private set; }
    public string ActualSetsJson { get; private set; } = "[]";
    public string ActualRepsJson { get; private set; } = "[]";
    public string ActualWeightKgJson { get; private set; } = "[]";
    public string ActualDurationSecJson { get; private set; } = "[]";
    public string? Notes { get; private set; }
    public int? Rpe { get; private set; }

    public Member Member { get; private set; } = null!;

    private WorkoutLog() { }

    public static WorkoutLog Create(
        Guid companyId,
        Guid siteId,
        Guid memberId,
        Guid routineItemId,
        DateTime performedAt,
        string setsJson,
        string repsJson,
        string weightJson) =>
        new()
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            RoutineItemId = routineItemId,
            PerformedAt = performedAt,
            ActualSetsJson = setsJson,
            ActualRepsJson = repsJson,
            ActualWeightKgJson = weightJson
        };
}
