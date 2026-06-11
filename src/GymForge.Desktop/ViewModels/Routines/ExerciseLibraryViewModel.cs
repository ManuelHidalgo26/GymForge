using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GymForge.Application.UseCases.Exercises;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Routines;

/// <summary>
/// Biblioteca de ejercicios (v1 de Rutinas): búsqueda + filtro por grupo muscular.
/// El armado de rutinas por socio se construye sobre esta base.
/// </summary>
public partial class ExerciseLibraryViewModel : ObservableObject
{
    private readonly IMediator _mediator;

    [ObservableProperty] private ObservableCollection<ExerciseDto> _exercises = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MuscleGroup? _muscleFilter;
    [ObservableProperty] private bool _isLoading;

    public IReadOnlyList<MuscleGroup> MuscleGroups { get; } = Enum.GetValues<MuscleGroup>().ToList();

    public bool IsEmpty => !IsLoading && Exercises.Count == 0;

    public ExerciseLibraryViewModel(IMediator mediator) => _mediator = mediator;

    partial void OnExercisesChanged(ObservableCollection<ExerciseDto> value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnSearchTextChanged(string value) => _ = LoadAsync();
    partial void OnMuscleFilterChanged(MuscleGroup? value) => _ = LoadAsync();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            Exercises = new ObservableCollection<ExerciseDto>(
                await _mediator.Send(new SearchExercisesQuery(SearchText, MuscleFilter), ct));
        }
        finally { IsLoading = false; }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        MuscleFilter = null;
    }
}
