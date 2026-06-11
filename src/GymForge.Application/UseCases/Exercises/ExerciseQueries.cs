using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Exercises;

public record ExerciseDto(
    Guid Id, string Name, MuscleGroup Muscle, Equipment Equipment, MovementType Movement, int Difficulty)
{
    public static ExerciseDto FromEntity(Exercise e) =>
        new(e.Id, e.Name, e.PrimaryMuscleGroup, e.Equipment, e.MovementType, e.Difficulty);
}

public record SearchExercisesQuery(string? Query, MuscleGroup? Muscle) : IRequest<IReadOnlyList<ExerciseDto>>;

public class SearchExercisesQueryHandler : IRequestHandler<SearchExercisesQuery, IReadOnlyList<ExerciseDto>>
{
    private readonly IExerciseRepository _repo;
    public SearchExercisesQueryHandler(IExerciseRepository repo) => _repo = repo;

    public async Task<IReadOnlyList<ExerciseDto>> Handle(SearchExercisesQuery q, CancellationToken ct) =>
        (await _repo.SearchAsync(q.Query, q.Muscle, ct: ct)).Select(ExerciseDto.FromEntity).ToList();
}

// ── Crear ejercicio ───────────────────────────────────────────────────────────

public record CreateExerciseCommand(
    string Name, MuscleGroup Muscle, Equipment Equipment, MovementType Movement,
    int Difficulty, Guid? TenantId = null) : IRequest<ExerciseDto>;

public class CreateExerciseCommandValidator : AbstractValidator<CreateExerciseCommand>
{
    public CreateExerciseCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre del ejercicio es obligatorio.");
        RuleFor(x => x.Difficulty).InclusiveBetween(1, 5).WithMessage("La dificultad va de 1 a 5.");
    }
}

public class CreateExerciseCommandHandler : IRequestHandler<CreateExerciseCommand, ExerciseDto>
{
    private readonly IExerciseRepository _repo;
    public CreateExerciseCommandHandler(IExerciseRepository repo) => _repo = repo;

    public async Task<ExerciseDto> Handle(CreateExerciseCommand cmd, CancellationToken ct)
    {
        var exercise = Exercise.Create(
            cmd.Name.Trim(), cmd.Muscle, cmd.Equipment, cmd.Movement, cmd.Difficulty, cmd.TenantId);
        await _repo.AddAsync(exercise, ct);
        await _repo.SaveChangesAsync(ct);
        return ExerciseDto.FromEntity(exercise);
    }
}

// ── Modificar ejercicio ───────────────────────────────────────────────────────

public record UpdateExerciseCommand(
    Guid Id, string Name, MuscleGroup Muscle, Equipment Equipment, MovementType Movement,
    int Difficulty) : IRequest<ExerciseDto>;

public class UpdateExerciseCommandValidator : AbstractValidator<UpdateExerciseCommand>
{
    public UpdateExerciseCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).WithMessage("El nombre del ejercicio es obligatorio.");
        RuleFor(x => x.Difficulty).InclusiveBetween(1, 5).WithMessage("La dificultad va de 1 a 5.");
    }
}

public class UpdateExerciseCommandHandler : IRequestHandler<UpdateExerciseCommand, ExerciseDto>
{
    private readonly IExerciseRepository _repo;
    public UpdateExerciseCommandHandler(IExerciseRepository repo) => _repo = repo;

    public async Task<ExerciseDto> Handle(UpdateExerciseCommand cmd, CancellationToken ct)
    {
        var exercise = await _repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException("El ejercicio no existe.");

        exercise.Update(cmd.Name.Trim(), cmd.Muscle, cmd.Equipment, cmd.Movement, cmd.Difficulty);
        await _repo.SaveChangesAsync(ct);
        return ExerciseDto.FromEntity(exercise);
    }
}

// ── Eliminar ejercicio ────────────────────────────────────────────────────────

public record DeleteExerciseCommand(Guid Id) : IRequest;

public class DeleteExerciseCommandHandler : IRequestHandler<DeleteExerciseCommand>
{
    private readonly IExerciseRepository _repo;
    public DeleteExerciseCommandHandler(IExerciseRepository repo) => _repo = repo;

    public async Task Handle(DeleteExerciseCommand cmd, CancellationToken ct)
    {
        var exercise = await _repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException("El ejercicio no existe.");

        if (await _repo.IsInUseAsync(cmd.Id, ct))
            throw new InvalidOperationException(
                "El ejercicio está usado en rutinas y no se puede eliminar.");

        _repo.Remove(exercise);
        await _repo.SaveChangesAsync(ct);
    }
}
