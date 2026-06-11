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
