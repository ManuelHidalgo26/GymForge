using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class ShiftRepository : IShiftRepository
{
    private readonly GymForgeDbContext _db;
    public ShiftRepository(GymForgeDbContext db) => _db = db;

    public async Task<Shift?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Shifts
            .Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<Shift?> GetOpenForSiteAsync(Guid siteId, CancellationToken ct = default) =>
        await _db.Shifts
            .Include(s => s.Movements)
            .FirstOrDefaultAsync(s => s.SiteId == siteId && s.Status == ShiftStatus.Open, ct);

    public async Task AddAsync(Shift shift, CancellationToken ct = default) =>
        await _db.Shifts.AddAsync(shift, ct);

    public async Task AddMovementAsync(CashMovement movement, CancellationToken ct = default) =>
        await _db.CashMovements.AddAsync(movement, ct);

    public void Update(Shift shift) => _db.Shifts.Update(shift);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
