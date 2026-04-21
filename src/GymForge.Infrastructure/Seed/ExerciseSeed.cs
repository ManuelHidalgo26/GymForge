using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Infrastructure.Seed;

public static class ExerciseSeed
{
    public static IReadOnlyList<Exercise> GetGlobalExercises()
    {
        static Exercise E(string name, MuscleGroup muscle, Equipment eq, MovementType mv, int diff = 3, bool unilateral = false, bool timed = false)
        {
            var e = Exercise.Create(name, muscle, eq, mv, diff);
            return e;
        }

        return new List<Exercise>
        {
            // ── PECHO ─────────────────────────────────────────────────────────────
            E("Press de Banca Plano con Barra", MuscleGroup.Chest, Equipment.Barbell, MovementType.Compound, 3),
            E("Press de Banca Inclinado con Barra", MuscleGroup.Chest, Equipment.Barbell, MovementType.Compound, 3),
            E("Press de Banca Declinado con Barra", MuscleGroup.Chest, Equipment.Barbell, MovementType.Compound, 3),
            E("Press de Banca con Mancuernas", MuscleGroup.Chest, Equipment.Dumbbell, MovementType.Compound, 2),
            E("Press Inclinado con Mancuernas", MuscleGroup.Chest, Equipment.Dumbbell, MovementType.Compound, 2),
            E("Apertura con Mancuernas (Fly)", MuscleGroup.Chest, Equipment.Dumbbell, MovementType.Isolation, 2),
            E("Cruce de Poleas Alto", MuscleGroup.Chest, Equipment.Cable, MovementType.Isolation, 2),
            E("Cruce de Poleas Bajo", MuscleGroup.Chest, Equipment.Cable, MovementType.Isolation, 2),
            E("Fondos en Paralelas (Pecho)", MuscleGroup.Chest, Equipment.Bodyweight, MovementType.Compound, 3),
            E("Flexiones de Brazos", MuscleGroup.Chest, Equipment.Bodyweight, MovementType.Compound, 1),
            E("Flexiones Inclinadas", MuscleGroup.Chest, Equipment.Bodyweight, MovementType.Compound, 2),
            E("Press en Máquina (Pecho)", MuscleGroup.Chest, Equipment.Machine, MovementType.Compound, 1),
            E("Pec Deck / Mariposa", MuscleGroup.Chest, Equipment.Machine, MovementType.Isolation, 1),

            // ── ESPALDA ───────────────────────────────────────────────────────────
            E("Peso Muerto Convencional", MuscleGroup.Back, Equipment.Barbell, MovementType.Compound, 4),
            E("Peso Muerto Rumano", MuscleGroup.Back, Equipment.Barbell, MovementType.Compound, 3),
            E("Remo con Barra", MuscleGroup.Back, Equipment.Barbell, MovementType.Compound, 3),
            E("Remo con Mancuerna", MuscleGroup.Back, Equipment.Dumbbell, MovementType.Compound, 2, unilateral: true),
            E("Dominadas (Agarre Pronado)", MuscleGroup.Lats, Equipment.Bodyweight, MovementType.Compound, 4),
            E("Jalón al Pecho (Polea Alta)", MuscleGroup.Lats, Equipment.Cable, MovementType.Compound, 2),
            E("Jalón al Pecho Trasnuca", MuscleGroup.Lats, Equipment.Cable, MovementType.Compound, 2),
            E("Remo en Polea Baja Sentado", MuscleGroup.Back, Equipment.Cable, MovementType.Compound, 2),
            E("Remo en Máquina", MuscleGroup.Back, Equipment.Machine, MovementType.Compound, 1),
            E("Pull-over con Mancuerna", MuscleGroup.Lats, Equipment.Dumbbell, MovementType.Isolation, 2),
            E("Jalón con Brazos Rectos (Polea)", MuscleGroup.Lats, Equipment.Cable, MovementType.Isolation, 2),
            E("Hiperextensión de Espalda Baja", MuscleGroup.Back, Equipment.Bodyweight, MovementType.Isolation, 2),
            E("Good Morning con Barra", MuscleGroup.Back, Equipment.Barbell, MovementType.Compound, 3),

            // ── HOMBROS ───────────────────────────────────────────────────────────
            E("Press Militar con Barra (De Pie)", MuscleGroup.Shoulders, Equipment.Barbell, MovementType.Compound, 3),
            E("Press Arnold con Mancuernas", MuscleGroup.Shoulders, Equipment.Dumbbell, MovementType.Compound, 2),
            E("Press de Hombros con Mancuernas", MuscleGroup.Shoulders, Equipment.Dumbbell, MovementType.Compound, 2),
            E("Elevaciones Laterales con Mancuernas", MuscleGroup.Shoulders, Equipment.Dumbbell, MovementType.Isolation, 1),
            E("Elevaciones Frontales con Mancuernas", MuscleGroup.Shoulders, Equipment.Dumbbell, MovementType.Isolation, 1),
            E("Pájaro / Elevaciones Posteriores", MuscleGroup.Shoulders, Equipment.Dumbbell, MovementType.Isolation, 2),
            E("Face Pull en Polea", MuscleGroup.Shoulders, Equipment.Cable, MovementType.Isolation, 2),
            E("Encogimiento de Hombros (Trapecios)", MuscleGroup.Traps, Equipment.Barbell, MovementType.Isolation, 1),

            // ── BÍCEPS ────────────────────────────────────────────────────────────
            E("Curl con Barra", MuscleGroup.Biceps, Equipment.Barbell, MovementType.Isolation, 1),
            E("Curl con Mancuernas Alternado", MuscleGroup.Biceps, Equipment.Dumbbell, MovementType.Isolation, 1, unilateral: true),
            E("Curl Martillo", MuscleGroup.Biceps, Equipment.Dumbbell, MovementType.Isolation, 1),
            E("Curl Predicador con Barra", MuscleGroup.Biceps, Equipment.Barbell, MovementType.Isolation, 2),
            E("Curl en Polea Baja", MuscleGroup.Biceps, Equipment.Cable, MovementType.Isolation, 1),
            E("Curl Concentrado", MuscleGroup.Biceps, Equipment.Dumbbell, MovementType.Isolation, 1, unilateral: true),

            // ── TRÍCEPS ───────────────────────────────────────────────────────────
            E("Press Cerrado con Barra", MuscleGroup.Triceps, Equipment.Barbell, MovementType.Compound, 3),
            E("Fondos en Banco", MuscleGroup.Triceps, Equipment.Bodyweight, MovementType.Isolation, 1),
            E("Extensión de Tríceps con Polea Alta", MuscleGroup.Triceps, Equipment.Cable, MovementType.Isolation, 1),
            E("Rompecráneos con Barra EZ", MuscleGroup.Triceps, Equipment.Barbell, MovementType.Isolation, 2),
            E("Patada de Tríceps con Mancuerna", MuscleGroup.Triceps, Equipment.Dumbbell, MovementType.Isolation, 1, unilateral: true),

            // ── PIERNAS ───────────────────────────────────────────────────────────
            E("Sentadilla con Barra", MuscleGroup.Quads, Equipment.Barbell, MovementType.Compound, 4),
            E("Sentadilla Sumo con Barra", MuscleGroup.Quads, Equipment.Barbell, MovementType.Compound, 3),
            E("Prensa de Piernas", MuscleGroup.Quads, Equipment.Machine, MovementType.Compound, 2),
            E("Extensión de Cuádriceps en Máquina", MuscleGroup.Quads, Equipment.Machine, MovementType.Isolation, 1),
            E("Curl Femoral Tumbado", MuscleGroup.Hamstrings, Equipment.Machine, MovementType.Isolation, 1),
            E("Curl Femoral de Pie", MuscleGroup.Hamstrings, Equipment.Machine, MovementType.Isolation, 1, unilateral: true),
            E("Hip Thrust con Barra", MuscleGroup.Glutes, Equipment.Barbell, MovementType.Isolation, 3),
            E("Estocada / Zancada con Mancuernas", MuscleGroup.Quads, Equipment.Dumbbell, MovementType.Compound, 2, unilateral: true),
            E("Sentadilla Búlgara", MuscleGroup.Quads, Equipment.Dumbbell, MovementType.Compound, 3, unilateral: true),
            E("Peso Muerto con Piernas Juntas", MuscleGroup.Hamstrings, Equipment.Barbell, MovementType.Compound, 3),
            E("Elevación de Gemelos de Pie", MuscleGroup.Calves, Equipment.Machine, MovementType.Isolation, 1),
            E("Elevación de Gemelos Sentado", MuscleGroup.Calves, Equipment.Machine, MovementType.Isolation, 1),

            // ── ABDOMEN ───────────────────────────────────────────────────────────
            E("Plancha Abdominal", MuscleGroup.Abs, Equipment.Bodyweight, MovementType.Isolation, 1, timed: true),
            E("Crunches Abdominales", MuscleGroup.Abs, Equipment.Bodyweight, MovementType.Isolation, 1),
            E("Elevación de Piernas Colgado", MuscleGroup.Abs, Equipment.Bodyweight, MovementType.Isolation, 3),
            E("Rueda Abdominal", MuscleGroup.Abs, Equipment.Other, MovementType.Isolation, 4),
            E("Crunch en Polea Alta", MuscleGroup.Abs, Equipment.Cable, MovementType.Isolation, 2),
            E("Russian Twist con Mancuerna", MuscleGroup.Obliques, Equipment.Dumbbell, MovementType.Isolation, 2),
            E("Plancha Lateral", MuscleGroup.Obliques, Equipment.Bodyweight, MovementType.Isolation, 2, timed: true),

            // ── CARDIO / FUNCIONAL ────────────────────────────────────────────────
            E("Cinta Trotadora", MuscleGroup.Cardio, Equipment.Machine, MovementType.Cardio, 1, timed: true),
            E("Bicicleta Estática", MuscleGroup.Cardio, Equipment.Machine, MovementType.Cardio, 1, timed: true),
            E("Elíptica", MuscleGroup.Cardio, Equipment.Machine, MovementType.Cardio, 1, timed: true),
            E("Remo Ergómetro", MuscleGroup.Cardio, Equipment.Machine, MovementType.Cardio, 2, timed: true),
            E("Salto a la Soga", MuscleGroup.Cardio, Equipment.Other, MovementType.Cardio, 2, timed: true),
            E("Burpees", MuscleGroup.FullBody, Equipment.Bodyweight, MovementType.Cardio, 3),
            E("Box Jump (Salto al Cajón)", MuscleGroup.Quads, Equipment.Other, MovementType.Compound, 3),
            E("Kettlebell Swing", MuscleGroup.Glutes, Equipment.Kettlebell, MovementType.Compound, 3),
            E("Thruster con Mancuernas", MuscleGroup.FullBody, Equipment.Dumbbell, MovementType.Compound, 4),
            E("Clean & Press con Barra", MuscleGroup.FullBody, Equipment.Barbell, MovementType.Compound, 5),

            // ── MOVILIDAD ─────────────────────────────────────────────────────────
            E("Estiramiento de Cuádriceps", MuscleGroup.Quads, Equipment.Bodyweight, MovementType.Mobility, 1, unilateral: true, timed: true),
            E("Estiramiento de Isquiotibiales", MuscleGroup.Hamstrings, Equipment.Bodyweight, MovementType.Mobility, 1, timed: true),
            E("Estiramiento de Pectoral", MuscleGroup.Chest, Equipment.Bodyweight, MovementType.Mobility, 1, timed: true),
            E("Movilidad de Cadera (90/90)", MuscleGroup.Glutes, Equipment.Bodyweight, MovementType.Mobility, 2, timed: true),
            E("Cat-Cow (Columna)", MuscleGroup.Back, Equipment.Bodyweight, MovementType.Mobility, 1, timed: true),
            E("World's Greatest Stretch", MuscleGroup.FullBody, Equipment.Bodyweight, MovementType.Mobility, 2, timed: true),
        };
    }
}
