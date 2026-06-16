namespace GymForge.Desktop.ViewModels.Routines;

/// <summary>
/// Hub de Rutinas: dos pestañas — el armador por socio y la biblioteca de ejercicios.
/// </summary>
public class RoutinesViewModel
{
    public RoutineBuilderViewModel Builder { get; }
    public ExerciseLibraryViewModel Library { get; }

    public RoutinesViewModel(RoutineBuilderViewModel builder, ExerciseLibraryViewModel library)
    {
        Builder = builder;
        Library = library;
    }
}
