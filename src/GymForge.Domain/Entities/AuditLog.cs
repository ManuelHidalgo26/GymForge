namespace GymForge.Domain.Entities;

/// <summary>Append-only audit log. Never update or delete rows.</summary>
public class AuditLog
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid? UserId { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public Guid EntityId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string? DiffJson { get; private set; }
    public string? Ip { get; private set; }
    public DateTime Timestamp { get; private set; } = DateTime.UtcNow;
    public Guid CompanyId { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(
        Guid companyId,
        Guid? userId,
        string entityType,
        Guid entityId,
        string action,
        string? diffJson = null,
        string? ip = null) =>
        new()
        {
            CompanyId = companyId,
            UserId = userId,
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            DiffJson = diffJson,
            Ip = ip
        };
}

public class WorkoutLog : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid RoutineItemId { get; private set; }
    public Guid CompanyId { get; private set; }
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
        Guid memberId,
        Guid routineItemId,
        DateTime performedAt,
        string setsJson,
        string repsJson,
        string weightJson) =>
        new()
        {
            CompanyId = companyId,
            MemberId = memberId,
            RoutineItemId = routineItemId,
            PerformedAt = performedAt,
            ActualSetsJson = setsJson,
            ActualRepsJson = repsJson,
            ActualWeightKgJson = weightJson
        };
}
