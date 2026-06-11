using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.UseCases.Exercises;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Routines;

/// <summary>
/// Biblioteca de ejercicios sueltos: búsqueda, filtro por grupo muscular y
/// ABM (crear, modificar, eliminar). Las rutinas por socio se arman sobre esto.
/// </summary>
public partial class ExerciseLibraryViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly SessionContext _session;
    private Guid? _editingId;   // null = creando

    [ObservableProperty] private ObservableCollection<ExerciseDto> _exercises = [];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private MuscleGroup? _muscleFilter;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Formulario alta/edición
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _formTitle = "Nuevo ejercicio";
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private MuscleGroup _muscle = MuscleGroup.Chest;
    [ObservableProperty] private Equipment _equipment = Equipment.Barbell;
    [ObservableProperty] private MovementType _movement = MovementType.Compound;
    [ObservableProperty] private int _difficulty = 3;

    public IReadOnlyList<MuscleGroup> MuscleGroups { get; } = Enum.GetValues<MuscleGroup>().ToList();
    public IReadOnlyList<Equipment> EquipmentOptions { get; } = Enum.GetValues<Equipment>().ToList();
    public IReadOnlyList<MovementType> MovementOptions { get; } = Enum.GetValues<MovementType>().ToList();

    public bool IsEmpty => !IsLoading && Exercises.Count == 0;

    public ExerciseLibraryViewModel(IMediator mediator, SessionContext session)
    {
        _mediator = mediator;
        _session = session;
    }

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

    // ── ABM ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void OpenCreateForm()
    {
        _editingId = null;
        FormTitle = "Nuevo ejercicio";
        Name = string.Empty;
        Muscle = MuscleGroup.Chest;
        Equipment = Equipment.Barbell;
        Movement = MovementType.Compound;
        Difficulty = 3;
        ErrorMessage = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void EditExercise(ExerciseDto? dto)
    {
        if (dto is null) return;
        _editingId = dto.Id;
        FormTitle = $"Modificar: {dto.Name}";
        Name = dto.Name;
        Muscle = dto.Muscle;
        Equipment = dto.Equipment;
        Movement = dto.Movement;
        Difficulty = dto.Difficulty;
        ErrorMessage = null;
        IsFormOpen = true;
    }

    [RelayCommand]
    private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task SaveAsync()
    {
        ErrorMessage = null;
        try
        {
            if (_editingId is { } id)
                await _mediator.Send(new UpdateExerciseCommand(id, Name, Muscle, Equipment, Movement, Difficulty));
            else
                await _mediator.Send(new CreateExerciseCommand(
                    Name, Muscle, Equipment, Movement, Difficulty, _session.CompanyId));

            IsFormOpen = false;
            await LoadAsync();
        }
        catch (ValidationException vex)
        {
            ErrorMessage = string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task DeleteExerciseAsync(ExerciseDto? dto)
    {
        if (dto is null) return;
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new DeleteExerciseCommand(dto.Id));
            await LoadAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }
}
