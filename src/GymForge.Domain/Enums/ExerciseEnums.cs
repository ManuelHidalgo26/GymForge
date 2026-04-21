namespace GymForge.Domain.Enums;

public enum MuscleGroup
{
    Chest,
    Back,
    Shoulders,
    Biceps,
    Triceps,
    Forearms,
    Quads,
    Hamstrings,
    Glutes,
    Calves,
    Abs,
    Obliques,
    Traps,
    Lats,
    Cardio,
    FullBody
}

public enum Equipment
{
    Barbell,
    Dumbbell,
    Machine,
    Cable,
    Bodyweight,
    Band,
    Kettlebell,
    Other
}

public enum MovementType
{
    Compound,
    Isolation,
    Cardio,
    Mobility
}

public enum ExerciseTechnique
{
    Normal,
    Superset,
    Dropset,
    Failure,
    RestPause
}

public enum WorkoutGoal
{
    Hypertrophy,
    FatLoss,
    Strength,
    Endurance,
    Mobility
}

public enum BodyMeasurementMethod
{
    Manual,
    Caliper,
    Bioimpedance,
    Dexa
}
