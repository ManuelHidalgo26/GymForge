using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Routine : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid? TrainerId { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public WorkoutGoal Goal { get; private set; }
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public int FrequencyPerWeek { get; private set; }
    public int Difficulty { get; private set; } = 3;
    public string? Notes { get; private set; }
    public Guid? TemplateId { get; private set; }
    public bool IsActive { get; private set; } = true;

    public Member Member { get; private set; } = null!;
    public ICollection<RoutineDay> Days { get; private set; } = [];

    private Routine() { }

    public static Routine Create(
        Guid companyId,
        Guid memberId,
        string name,
        WorkoutGoal goal,
        DateOnly startDate,
        int frequencyPerWeek,
        Guid? trainerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Routine
        {
            CompanyId = companyId,
            MemberId = memberId,
            Name = name,
            Goal = goal,
            StartDate = startDate,
            FrequencyPerWeek = frequencyPerWeek,
            TrainerId = trainerId
        };
    }
}

public class RoutineDay : BaseEntity
{
    public Guid RoutineId { get; private set; }
    public int DayNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;

    public Routine Routine { get; private set; } = null!;
    public ICollection<RoutineItem> Items { get; private set; } = [];

    private RoutineDay() { }

    public static RoutineDay Create(Guid routineId, int dayNumber, string name) =>
        new() { RoutineId = routineId, DayNumber = dayNumber, Name = name };
}

public class RoutineItem : BaseEntity
{
    public Guid RoutineDayId { get; private set; }
    public Guid ExerciseId { get; private set; }
    public int Rank { get; private set; }
    public int RestSeconds { get; private set; } = 60;
    public string? Tempo { get; private set; }
    public ExerciseTechnique Technique { get; private set; } = ExerciseTechnique.Normal;
    public string? Notes { get; private set; }

    public RoutineDay RoutineDay { get; private set; } = null!;
    public Exercise Exercise { get; private set; } = null!;
    public ICollection<RoutineItemSet> Sets { get; private set; } = [];

    private RoutineItem() { }

    public static RoutineItem Create(Guid routineDayId, Guid exerciseId, int rank) =>
        new() { RoutineDayId = routineDayId, ExerciseId = exerciseId, Rank = rank };
}

public class RoutineItemSet : BaseEntity
{
    public Guid RoutineItemId { get; private set; }
    public int SetNumber { get; private set; }
    public int? TargetRepsMin { get; private set; }
    public int? TargetRepsMax { get; private set; }
    public decimal? TargetWeightKg { get; private set; }
    public int? TargetDurationSec { get; private set; }
    public int? TargetRpe { get; private set; }

    public RoutineItem RoutineItem { get; private set; } = null!;

    private RoutineItemSet() { }

    public static RoutineItemSet Create(Guid routineItemId, int setNumber, int? repsMin = null, int? repsMax = null) =>
        new() { RoutineItemId = routineItemId, SetNumber = setNumber, TargetRepsMin = repsMin, TargetRepsMax = repsMax };
}
