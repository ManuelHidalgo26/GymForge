using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class ClassRepository : IClassRepository
{
    private readonly GymForgeDbContext _db;
    public ClassRepository(GymForgeDbContext db) => _db = db;

    public async Task<IReadOnlyList<ClassDescription>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.ClassDescriptions
            .Where(c => c.CompanyId == companyId && c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

    public async Task AddAsync(ClassDescription cls, CancellationToken ct = default) =>
        await _db.ClassDescriptions.AddAsync(cls, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}

public class ExerciseRepository : IExerciseRepository
{
    private readonly GymForgeDbContext _db;
    public ExerciseRepository(GymForgeDbContext db) => _db = db;

    public async Task<IReadOnlyList<Exercise>> SearchAsync(
        string? query, MuscleGroup? muscle, int take = 300, CancellationToken ct = default)
    {
        var q = _db.Exercises.AsQueryable();

        if (muscle is { } m)
            q = q.Where(e => e.PrimaryMuscleGroup == m);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var lower = query.Trim().ToLowerInvariant();
            q = q.Where(e => e.Name.ToLower().Contains(lower));
        }

        return await q.OrderBy(e => e.Name).Take(take).ToListAsync(ct);
    }
}
