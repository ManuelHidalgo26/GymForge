using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Classes;
using GymForge.Desktop.Services;
using MediatR;

namespace GymForge.Desktop.ViewModels.Classes;

/// <summary>
/// Clases v2: catálogo de clases + agenda semanal de horarios + reservas de socios.
/// </summary>
public partial class ClassesViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly IMemberRepository _memberRepo;
    private readonly SessionContext _session;
    private readonly ICompanyCalendar _calendar;
    private static readonly string[] DayNames = ["Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom"];

    private bool _weekInitialized;
    private DateOnly _today;

    [ObservableProperty] private ObservableCollection<ClassDto> _classes = [];
    [ObservableProperty] private ObservableCollection<ScheduleRowDto> _schedules = [];
    [ObservableProperty] private ObservableCollection<ScheduleDayVm> _weekDays = [];
    [ObservableProperty] private ObservableCollection<BookingRowDto> _bookings = [];
    [ObservableProperty] private ObservableCollection<MemberDto> _members = [];

    [ObservableProperty] private ScheduleRowDto? _selectedSchedule;
    [ObservableProperty] private MemberDto? _selectedMember;
    [ObservableProperty] private DateOnly _weekStart;

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;

    // Form: nueva clase (catálogo)
    [ObservableProperty] private bool _isFormOpen;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private int _durationMin = 60;
    [ObservableProperty] private int _capacity = 20;

    // Form: nuevo horario
    [ObservableProperty] private bool _isScheduleFormOpen;
    [ObservableProperty] private ClassDto? _scheduleClass;
    [ObservableProperty] private DateOnly? _scheduleDate;
    [ObservableProperty] private TimeSpan _scheduleTime = new(18, 0, 0);
    [ObservableProperty] private int _scheduleCapacity = 20;

    public bool IsEmpty => !IsLoading && Schedules.Count == 0;
    public bool HasSelection => SelectedSchedule is not null;
    public string WeekLabel => $"{WeekStart:dd/MM} – {WeekStart.AddDays(6):dd/MM}";

    public ClassesViewModel(
        IMediator mediator, IMemberRepository memberRepo, SessionContext session, ICompanyCalendar calendar)
    {
        _mediator = mediator;
        _memberRepo = memberRepo;
        _session = session;
        _calendar = calendar;
    }

    partial void OnSchedulesChanged(ObservableCollection<ScheduleRowDto> value)
    {
        OnPropertyChanged(nameof(IsEmpty));
        BuildWeekDays();
    }

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));
    partial void OnWeekStartChanged(DateOnly value) => OnPropertyChanged(nameof(WeekLabel));

    partial void OnSelectedScheduleChanged(ScheduleRowDto? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = LoadBookingsAsync();
    }

    [RelayCommand]
    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            await EnsureCurrentWeekAsync(ct);
            Classes = new ObservableCollection<ClassDto>(
                await _mediator.Send(new GetClassesQuery(_session.CompanyId), ct));

            var members = await _memberRepo.GetPagedAsync(_session.CompanyId, _session.SiteId, 1, 500, ct: ct);
            Members = new ObservableCollection<MemberDto>(members.Select(MemberDto.FromEntity));

            await LoadSchedulesAsync(ct);
        }
        finally { IsLoading = false; }
    }

    /// <summary>La agenda arranca en la semana del día del gimnasio (no el de la zona de la PC).</summary>
    private async Task EnsureCurrentWeekAsync(CancellationToken ct)
    {
        if (_weekInitialized) return;

        WeekStart = MondayOf(await RefreshTodayAsync(ct));
        ScheduleDate = WeekStart;
        _weekInitialized = true;
    }

    private async Task<DateOnly> RefreshTodayAsync(CancellationToken ct = default) =>
        _today = await _calendar.TodayAsync(_session.CompanyId, ct);

    /// <summary>Proyecta los horarios cargados en 7 columnas Lun–Dom (la query acota a la semana visible).</summary>
    private void BuildWeekDays()
    {
        var byDay = Schedules
            .GroupBy(s => DateOnly.FromDateTime(s.Start))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ScheduleRowDto>)[.. g.OrderBy(s => s.Start)]);

        WeekDays = new ObservableCollection<ScheduleDayVm>(
            Enumerable.Range(0, DayNames.Length).Select(i =>
            {
                var date = WeekStart.AddDays(i);
                return new ScheduleDayVm(date, DayNames[i], date == _today, byDay.GetValueOrDefault(date) ?? []);
            }));
    }

    private async Task LoadSchedulesAsync(CancellationToken ct = default)
    {
        var rows = await _mediator.Send(
            new GetWeekSchedulesQuery(_session.CompanyId, _session.SiteId, WeekStart), ct);
        Schedules = new ObservableCollection<ScheduleRowDto>(rows);

        // Mantener la selección si el horario sigue en la semana visible.
        SelectedSchedule = SelectedSchedule is { } sel
            ? Schedules.FirstOrDefault(s => s.Id == sel.Id)
            : null;
    }

    [RelayCommand] private void SelectSchedule(ScheduleRowDto row) => SelectedSchedule = row;

    private async Task LoadBookingsAsync(CancellationToken ct = default)
    {
        if (SelectedSchedule is null) { Bookings = []; return; }
        var rows = await _mediator.Send(
            new GetScheduleBookingsQuery(_session.CompanyId, SelectedSchedule.Id), ct);
        Bookings = new ObservableCollection<BookingRowDto>(rows);
    }

    [RelayCommand] private void PrevWeek() { WeekStart = WeekStart.AddDays(-7); _ = LoadSchedulesAsync(); }
    [RelayCommand] private void NextWeek() { WeekStart = WeekStart.AddDays(7); _ = LoadSchedulesAsync(); }

    [RelayCommand]
    private async Task ThisWeekAsync()
    {
        WeekStart = MondayOf(await RefreshTodayAsync());
        await LoadSchedulesAsync();
    }

    // ── Catálogo: nueva clase ───────────────────────────────────────────────
    [RelayCommand] private void OpenForm() { ErrorMessage = null; IsFormOpen = true; }
    [RelayCommand] private void CancelForm() => IsFormOpen = false;

    [RelayCommand]
    private async Task CreateAsync()
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new CreateClassCommand(_session.CompanyId, Name, DurationMin, Capacity));
            Name = string.Empty;
            DurationMin = 60;
            Capacity = 20;
            IsFormOpen = false;
            await LoadAsync();
        }
        catch (ValidationException vex) { ErrorMessage = Join(vex); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ── Agenda: nuevo horario ───────────────────────────────────────────────
    [RelayCommand]
    private void OpenScheduleForm()
    {
        ErrorMessage = null;
        ScheduleClass = Classes.FirstOrDefault();
        ScheduleCapacity = ScheduleClass?.Capacity ?? 20;
        ScheduleDate = WeekStart;
        IsScheduleFormOpen = true;
    }

    [RelayCommand] private void CancelScheduleForm() => IsScheduleFormOpen = false;

    partial void OnScheduleClassChanged(ClassDto? value)
    {
        if (value is not null) ScheduleCapacity = value.Capacity;
    }

    [RelayCommand]
    private async Task CreateScheduleAsync()
    {
        ErrorMessage = null;
        try
        {
            if (ScheduleClass is null) { ErrorMessage = "Elegí una clase."; return; }
            if (ScheduleDate is null) { ErrorMessage = "Elegí una fecha."; return; }

            var start = ScheduleDate.Value.ToDateTime(TimeOnly.FromTimeSpan(ScheduleTime));
            await _mediator.Send(new CreateScheduleCommand(
                _session.CompanyId, _session.SiteId, ScheduleClass.Id, start, ScheduleCapacity));

            IsScheduleFormOpen = false;
            await LoadSchedulesAsync();
        }
        catch (ValidationException vex) { ErrorMessage = Join(vex); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    // ── Reservas ────────────────────────────────────────────────────────────
    [RelayCommand]
    private async Task BookMemberAsync()
    {
        ErrorMessage = null;
        try
        {
            if (SelectedSchedule is null) { ErrorMessage = "Elegí un horario."; return; }
            if (SelectedMember is null) { ErrorMessage = "Elegí un socio."; return; }

            await _mediator.Send(new BookMemberCommand(
                _session.CompanyId, SelectedSchedule.Id, SelectedMember.Id));

            SelectedMember = null;
            await LoadBookingsAsync();
            await LoadSchedulesAsync();
        }
        catch (ValidationException vex) { ErrorMessage = Join(vex); }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    [RelayCommand]
    private async Task CancelBookingAsync(Guid bookingId)
    {
        ErrorMessage = null;
        try
        {
            await _mediator.Send(new CancelBookingCommand(_session.CompanyId, bookingId));
            await LoadBookingsAsync();
            await LoadSchedulesAsync();
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
    }

    private static string Join(ValidationException vex) =>
        string.Join("\n", vex.Errors.Select(e => e.ErrorMessage));

    /// <summary>Lunes de la semana de la fecha dada (semana arranca el lunes).</summary>
    private static DateOnly MondayOf(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
