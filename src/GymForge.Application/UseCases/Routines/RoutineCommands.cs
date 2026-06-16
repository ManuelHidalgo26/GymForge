using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Routines;

// Armador de rutinas por socio: Routine → RoutineDay → RoutineItem (+ series).

public record RoutineDto(Guid Id, string Name, WorkoutGoal Goal, int FrequencyPerWeek, int DayCount)
{
    public static RoutineDto FromEntity(Routine r) =>
        new(r.Id, r.Name, r.Goal, r.FrequencyPerWeek, r.Days.Count);
}

public record RoutineItemDto(Guid Id, string ExerciseName, string SetsSummary);
public record RoutineDayDto(Guid Id, int DayNumber, string Name, IReadOnlyList<RoutineItemDto> Items);
public record RoutineDetailDto(
    Guid Id, string Name, WorkoutGoal Goal, int FrequencyPerWeek, IReadOnlyList<RoutineDayDto> Days);

// ── Listar / crear rutina ─────────────────────────────────────────────────────

public record GetMemberRoutinesQuery(Guid CompanyId, Guid MemberId) : IRequest<IReadOnlyList<RoutineDto>>;

public class GetMemberRoutinesQueryHandler : IRequestHandler<GetMemberRoutinesQuery, IReadOnlyList<RoutineDto>>
{
    private readonly IRoutineRepository _repo;
    public GetMemberRoutinesQueryHandler(IRoutineRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<RoutineDto>> Handle(GetMemberRoutinesQuery q, CancellationToken ct) =>
        (await _repo.GetByMemberAsync(q.CompanyId, q.MemberId, ct)).Select(RoutineDto.FromEntity).ToList();
}

public record CreateRoutineCommand(
    Guid CompanyId, Guid MemberId, string Name, WorkoutGoal Goal, int FrequencyPerWeek) : IRequest<RoutineDto>;

public class CreateRoutineCommandValidator : AbstractValidator<CreateRoutineCommand>
{
    public CreateRoutineCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("Elegí un socio.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre de la rutina es obligatorio.");
        RuleFor(x => x.FrequencyPerWeek).InclusiveBetween(1, 7).WithMessage("La frecuencia va de 1 a 7 días.");
    }
}

public class CreateRoutineCommandHandler : IRequestHandler<CreateRoutineCommand, RoutineDto>
{
    private readonly IRoutineRepository _repo;
    public CreateRoutineCommandHandler(IRoutineRepository repo) => _repo = repo;

    public async Task<RoutineDto> Handle(CreateRoutineCommand cmd, CancellationToken ct)
    {
        var routine = Routine.Create(
            cmd.CompanyId, cmd.MemberId, cmd.Name.Trim(), cmd.Goal,
            DateOnly.FromDateTime(DateTime.Today), cmd.FrequencyPerWeek);
        await _repo.AddRoutineAsync(routine, ct);
        await _repo.SaveChangesAsync(ct);
        return RoutineDto.FromEntity(routine);
    }
}

// ── Detalle de la rutina ──────────────────────────────────────────────────────

public record GetRoutineDetailQuery(Guid CompanyId, Guid RoutineId) : IRequest<RoutineDetailDto?>;

public class GetRoutineDetailQueryHandler : IRequestHandler<GetRoutineDetailQuery, RoutineDetailDto?>
{
    private readonly IRoutineRepository _repo;
    public GetRoutineDetailQueryHandler(IRoutineRepository repo) => _repo = repo;

    public async Task<RoutineDetailDto?> Handle(GetRoutineDetailQuery q, CancellationToken ct)
    {
        var r = await _repo.GetDetailAsync(q.RoutineId, ct);
        if (r is null || r.CompanyId != q.CompanyId) return null;

        var days = r.Days.OrderBy(d => d.DayNumber).Select(d => new RoutineDayDto(
            d.Id, d.DayNumber, d.Name,
            d.Items.OrderBy(i => i.Rank)
                   .Select(i => new RoutineItemDto(i.Id, i.Exercise?.Name ?? "Ejercicio", Summarize(i)))
                   .ToList())).ToList();

        return new RoutineDetailDto(r.Id, r.Name, r.Goal, r.FrequencyPerWeek, days);
    }

    private static string Summarize(RoutineItem item)
    {
        if (item.Sets.Count == 0) return "—";
        var first = item.Sets.OrderBy(s => s.SetNumber).First();
        var reps = (first.TargetRepsMin, first.TargetRepsMax) switch
        {
            (int min, int max) when min != max => $"{min}-{max}",
            (int min, _) => $"{min}",
            _ => null,
        };
        return reps is null ? $"{item.Sets.Count} series" : $"{item.Sets.Count}×{reps}";
    }
}

// ── Agregar día ───────────────────────────────────────────────────────────────

public record AddRoutineDayCommand(Guid CompanyId, Guid RoutineId, string Name) : IRequest;

public class AddRoutineDayCommandHandler : IRequestHandler<AddRoutineDayCommand>
{
    private readonly IRoutineRepository _repo;
    public AddRoutineDayCommandHandler(IRoutineRepository repo) => _repo = repo;

    public async Task Handle(AddRoutineDayCommand cmd, CancellationToken ct)
    {
        var routine = await _repo.GetWithDaysAsync(cmd.RoutineId, ct);
        if (routine is null || routine.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("La rutina no existe.");

        var dayNumber = routine.Days.Count + 1;
        var name = string.IsNullOrWhiteSpace(cmd.Name) ? $"Día {dayNumber}" : cmd.Name.Trim();
        await _repo.AddDayAsync(RoutineDay.Create(routine.Id, dayNumber, name), ct);
        await _repo.SaveChangesAsync(ct);
    }
}

// ── Agregar ejercicio a un día ────────────────────────────────────────────────

public record AddRoutineItemCommand(
    Guid CompanyId, Guid RoutineDayId, Guid ExerciseId, int Sets, int RepsMin, int RepsMax) : IRequest;

public class AddRoutineItemCommandValidator : AbstractValidator<AddRoutineItemCommand>
{
    public AddRoutineItemCommandValidator()
    {
        RuleFor(x => x.ExerciseId).NotEmpty().WithMessage("Elegí un ejercicio.");
        RuleFor(x => x.Sets).InclusiveBetween(1, 12).WithMessage("Las series van de 1 a 12.");
        RuleFor(x => x.RepsMin).GreaterThan(0).WithMessage("Las reps deben ser positivas.");
        RuleFor(x => x.RepsMax).GreaterThanOrEqualTo(x => x.RepsMin).WithMessage("El máximo de reps no puede ser menor al mínimo.");
    }
}

public class AddRoutineItemCommandHandler : IRequestHandler<AddRoutineItemCommand>
{
    private readonly IRoutineRepository _repo;
    public AddRoutineItemCommandHandler(IRoutineRepository repo) => _repo = repo;

    public async Task Handle(AddRoutineItemCommand cmd, CancellationToken ct)
    {
        var day = await _repo.GetDayAsync(cmd.RoutineDayId, ct);
        if (day is null || day.Routine.CompanyId != cmd.CompanyId)
            throw new InvalidOperationException("El día de la rutina no existe.");

        var rank = day.Items.Count + 1;
        var item = RoutineItem.Create(day.Id, cmd.ExerciseId, rank);
        for (var n = 1; n <= cmd.Sets; n++)
            item.Sets.Add(RoutineItemSet.Create(item.Id, n, cmd.RepsMin, cmd.RepsMax));

        await _repo.AddItemAsync(item, ct);
        await _repo.SaveChangesAsync(ct);
    }
}
