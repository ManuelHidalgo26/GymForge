using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Exercise : BaseEntity
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string? Instructions { get; private set; }
    public MuscleGroup PrimaryMuscleGroup { get; private set; }
    public string SecondaryMuscleGroupsJson { get; private set; } = "[]";
    public Equipment Equipment { get; private set; }
    public MovementType MovementType { get; private set; }
    public int Difficulty { get; private set; } = 3;
    public string? VideoUrl { get; private set; }
    public string? ImageUrl { get; private set; }
    public string? AnimatedGifUrl { get; private set; }
    public bool IsUnilateral { get; private set; }
    public bool IsTimed { get; private set; }
    public Guid? TenantId { get; private set; }

    private Exercise() { }

    public static Exercise Create(
        string name,
        MuscleGroup primaryMuscleGroup,
        Equipment equipment,
        MovementType movementType,
        int difficulty = 3,
        Guid? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (difficulty < 1 || difficulty > 5)
            throw new ArgumentException("Difficulty must be between 1 and 5.");

        return new Exercise
        {
            Name = name,
            PrimaryMuscleGroup = primaryMuscleGroup,
            Equipment = equipment,
            MovementType = movementType,
            Difficulty = difficulty,
            TenantId = tenantId
        };
    }

    public bool IsGlobal => TenantId is null;
}
