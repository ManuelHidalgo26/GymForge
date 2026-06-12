using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class MembershipRepository : IMembershipRepository
{
    private readonly GymForgeDbContext _db;
    public MembershipRepository(GymForgeDbContext db) => _db = db;

    public async Task<Membership?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.MembershipType)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<Membership>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Memberships.ToListAsync(ct);

    public async Task AddAsync(Membership entity, CancellationToken ct = default) =>
        await _db.Memberships.AddAsync(entity, ct);

    public void Update(Membership entity) => _db.Memberships.Update(entity);
    public void Remove(Membership entity) => _db.Memberships.Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);

    public async Task<Membership?> GetCurrentActiveAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.MembershipType)
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Membership>> GetByMemberAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.MembershipType)
            .Where(m => m.MemberId == memberId)
            .OrderByDescending(m => m.StartDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Membership>> GetExpiringAsync(
        Guid companyId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.Member)
            .Include(m => m.MembershipType)
            .Where(m =>
                m.CompanyId == companyId &&
                m.Status == MembershipStatus.Active &&
                m.EndDate >= from &&
                m.EndDate <= to)
            .OrderBy(m => m.EndDate)
            .ToListAsync(ct);
}
