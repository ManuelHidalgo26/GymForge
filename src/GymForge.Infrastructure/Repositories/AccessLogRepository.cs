using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class AccessLogRepository : IAccessLogRepository
{
    private readonly GymForgeDbContext _db;
    public AccessLogRepository(GymForgeDbContext db) => _db = db;

    public async Task AppendAsync(AccessLog log, CancellationToken ct = default)
    {
        await _db.AccessLogs.AddAsync(log, ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<AccessLog?> GetLastAsync(Guid memberId, int doorId, CancellationToken ct = default) =>
        await _db.AccessLogs
            .Where(a => a.MemberId == memberId && a.DoorId == doorId)
            .OrderByDescending(a => a.SwipedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<AccessLog>> GetByMemberAsync(
        Guid memberId, int take = 50, CancellationToken ct = default) =>
        await _db.AccessLogs
            .Where(a => a.MemberId == memberId)
            .OrderByDescending(a => a.SwipedAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<AccessLog>> GetBySiteAsync(
        Guid siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.AccessLogs
            .Include(a => a.Member)
            .Where(a => a.SiteId == siteId && a.SwipedAt >= from && a.SwipedAt <= to)
            .OrderByDescending(a => a.SwipedAt)
            .ToListAsync(ct);
}
