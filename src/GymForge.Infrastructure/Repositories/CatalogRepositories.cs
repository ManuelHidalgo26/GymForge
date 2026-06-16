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

    public async Task<ClassDescription?> GetClassAsync(Guid classDescriptionId, CancellationToken ct = default) =>
        await _db.ClassDescriptions.FirstOrDefaultAsync(c => c.Id == classDescriptionId, ct);

    public async Task AddAsync(ClassDescription cls, CancellationToken ct = default) =>
        await _db.ClassDescriptions.AddAsync(cls, ct);

    public async Task AddScheduleAsync(ClassSchedule schedule, CancellationToken ct = default) =>
        await _db.ClassSchedules.AddAsync(schedule, ct);

    public async Task<ClassSchedule?> GetScheduleAsync(Guid scheduleId, CancellationToken ct = default) =>
        await _db.ClassSchedules
            .Include(s => s.ClassDescription)
            .Include(s => s.Bookings).ThenInclude(b => b.Member)
            .FirstOrDefaultAsync(s => s.Id == scheduleId, ct);

    public async Task<IReadOnlyList<ClassSchedule>> GetSchedulesAsync(
        Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.ClassSchedules
            .Include(s => s.ClassDescription)
            .Include(s => s.Bookings)
            .Where(s => s.CompanyId == companyId && s.SiteId == siteId &&
                        s.StartDatetime >= from && s.StartDatetime < to)
            .OrderBy(s => s.StartDatetime)
            .ToListAsync(ct);

    public async Task AddBookingAsync(Booking booking, CancellationToken ct = default) =>
        await _db.Bookings.AddAsync(booking, ct);

    public async Task<Booking?> GetBookingAsync(Guid bookingId, CancellationToken ct = default) =>
        await _db.Bookings.FirstOrDefaultAsync(b => b.Id == bookingId, ct);

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

    public async Task<Exercise?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Exercises.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(Exercise exercise, CancellationToken ct = default) =>
        await _db.Exercises.AddAsync(exercise, ct);

    public void Remove(Exercise exercise) => _db.Exercises.Remove(exercise);

    public async Task<bool> IsInUseAsync(Guid exerciseId, CancellationToken ct = default) =>
        await _db.RoutineItems.AnyAsync(ri => ri.ExerciseId == exerciseId, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
