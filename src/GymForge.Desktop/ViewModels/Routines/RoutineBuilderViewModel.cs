using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Exercises;
using GymForge.Application.UseCases.Routines;
using GymForge.Desktop.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Desktop.ViewModels.Routines;

/// <summary>
/// Armador de rutinas por socio: elegir socio → crear/ver rutinas → agregar días
/// y ejercicios (de la biblioteca) con series y repeticiones.
/// </summary>
public partial class RoutineBuilderViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;

    [ObservableProperty] private ObservableCollection<MemberDto> _members = [];
    [ObservableProperty] private MemberDto? _selectedMember;
    [ObservableProperty] private ObservableCollection<RoutineDto> _routines = [];
    [ObservableProperty] private RoutineDto? _selectedRoutine;
    [ObservableProperty] private RoutineDetailDto? _detail;
    [ObservableProperty] private ObservableCollection<ExerciseDto> _exercises = [];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Form: nueva rutina
    [ObservableProperty] private bool _isRoutineFormOpen;
    [ObservableProperty] private string _routineName = string.Empty;
    [ObservableProperty] private WorkoutGoal _routineGoal = WorkoutGoal.Hypertrophy;
    [ObservableProperty] private int _routineFreq = 3;

    // Nuevo día
    [ObservableProperty] private string _newDayName = string.Empty;

    // Form: agregar ejercicio a un día
    [ObservableProperty] private bool _isItemFormOpen;
    [ObservableProperty] private RoutineDayDto? _itemDay;
    [ObservableProperty] private ExerciseDto? _itemExercise;
    [ObservableProperty] private int _itemSets = 3;
    [ObservableProperty] private int _itemRepsMin = 8;
    [ObservableProperty] private int _itemRepsMax = 12;

    public IReadOnlyList<WorkoutGoal> Goals { get; } = Enum.GetValues<WorkoutGoal>().ToList();
    public bool HasMember => SelectedMember is not null;
    public bool HasRoutine => Detail is not null;

    public RoutineBuilderViewModel(IMediator mediator, IMemberRepository memberRepo, SessionContext session)
    {
        _mediator = mediator;
        _memberRepo = memberRepo;
        _session = session;
    }

    partial void OnDetailChanged(RoutineDetailDto? value) => OnPropertyChanged(nameof(HasRoutine));

    partial void OnSelectedMemberChanged(MemberDto? value)
    {
        OnPropertyChanged(nameof(HasMember));
        SelectedRoutine = null;
        Detail = null;
        _ = LoadRoutinesAsync();
    }

    partial void OnSelectedRoutineChanged(RoutineDto? value) => _ = LoadDetailAsync();

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        // Idempotente: se dispara al asignar el DataContext (code-behind). Si ya
        // cargamos socios/ejercicios, no recargar para no pisar la selección.
        if (Members.Count > 0) return;

        IsLoading = true;
        ErrorMessage = null;
        try
        {
            var members = await _memberRepo.GetPagedAsync(_session.CompanyId, _session.SiteId, 1, 500, ct: ct);
            Members = new ObservableCollection<MemberDto>(members.Select(MemberDto.FromEntity));
            Exercises = new ObservableCollection<ExerciseDto>(
                await _mediator.Send(new SearchExercisesQuery(null, null), ct));
        }
        finally { IsLoading = false; }
    }

    private async Task LoadRoutinesAsync(CancellationToken ct = default)
    {
        if (SelectedMember is null) { Routines = []; return; }
        var rows = await _mediator.Send(new GetMemberRoutinesQuery(_session.CompanyId, SelectedMember.Id), ct);
        Routines = new ObservableCollection<RoutineDto>(rows);
    }

    private async Task LoadDetailAsync(CancellationToken ct = default)
    {
        if (SelectedRoutine is null) { Detail = null; return; }
        Detail = await _mediator.Send(new GetRoutineDetailQuery(_session.CompanyId, SelectedRoutine.Id), ct);
    }

    // ── Nueva rutina ─────────────────────────────────────────────────────────
    [RelayCommand]
    private void OpenRoutineForm()
    {
        ErrorMessage = null;
        RoutineName = string.Empty;
        RoutineGoal = WorkoutGoal.Hypertrophy;
        RoutineFreq = 3;
        IsRoutineFormOpen = true;
    }

    [RelayCommand] private void CancelRoutineForm() => IsRoutineFormOpen = false;

    [RelayCommand]
    private async Task CreateRoutineAsync()
    {
        ErrorMessage = null;
        try
        {
            if (SelectedMember is null) { ErrorMessage = "Elegí un socio."; return; }
            var dto = await _mediator.Send(new CreateRoutineCommand(
                _session.CompanyId, SelectedMember.Id, RoutineName, RoutineGoal, RoutineFreq));
            IsRoutineFormOpen = false;
            await LoadRoutinesAsync();
            SelectedRoutine = Routines.FirstOrDefault(r => r.Id == dto.Id);
        }
        catch (ValidationException vex) { ErrorMessage = Join(vex); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ── Días ──────────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task AddDayAsync()
    {
        ErrorMessage = null;
        try
        {
            if (SelectedRoutine is null) { ErrorMessage = "Elegí una rutina."; return; }
            await _mediator.Send(new AddRoutineDayCommand(_session.CompanyId, SelectedRoutine.Id, NewDayName));
            NewDayName = string.Empty;
            await LoadDetailAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ── Ejercicios ─────────────────────────────────────────────────────────────
    [RelayCommand]
    private void OpenItemForm(RoutineDayDto? day)
    {
        if (day is null) return;
        ErrorMessage = null;
        ItemDay = day;
        ItemExercise = Exercises.FirstOrDefault();
        ItemSets = 3;
        ItemRepsMin = 8;
        ItemRepsMax = 12;
        IsItemFormOpen = true;
    }

    [RelayCommand] private void CancelItemForm() => IsItemFormOpen = false;

    [RelayCommand]
    private async Task AddItemAsync()
    {
        ErrorMessage = null;
        try
        {
            if (ItemDay is null) { ErrorMessage = "Elegí un día."; return; }
            if (ItemExercise is null) { ErrorMessage = "Elegí un ejercicio."; return; }
            await _mediator.Send(new AddRoutineItemCommand(
                _session.CompanyId, ItemDay.Id, ItemExercise.Id, ItemSets, ItemRepsMin, ItemRepsMax));
            IsItemFormOpen = false;
            await LoadDetailAsync();
        }
        catch (ValidationException vex) { ErrorMessage = Join(vex); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private static string Join(ValidationException vex) =>
        string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));
}
