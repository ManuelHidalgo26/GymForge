using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

/// <summary>
/// Registro de recordatorios de cobranza. Append-only a propósito: no hay Update ni Remove.
/// Las lecturas por socio pasan por el filtro global de tenant (empresa + sede).
/// </summary>
public class NotificationLogRepository : INotificationLogRepository
{
    private readonly GymForgeDbContext _db;
    public NotificationLogRepository(GymForgeDbContext db) => _db = db;

    public async Task AppendAsync(NotificationLog log, CancellationToken ct = default) =>
        await _db.NotificationLogs.AddAsync(log, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);

    public async Task<IReadOnlyList<NotificationLog>> GetByMemberAsync(
        Guid memberId, int take = 50, CancellationToken ct = default) =>
        await _db.NotificationLogs
            .Where(n => n.MemberId == memberId)
            .OrderByDescending(n => n.SentAt)
            .Take(take)
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastManualByMembersAsync(
        IReadOnlyCollection<Guid> memberIds, CancellationToken ct = default) =>
        await _db.NotificationLogs
            .Where(n => n.Trigger == NotificationTrigger.Manual && memberIds.Contains(n.MemberId))
            .GroupBy(n => n.MemberId)
            .Select(g => new { MemberId = g.Key, LastSentAt = g.Max(n => n.SentAt) })
            .ToDictionaryAsync(x => x.MemberId, x => x.LastSentAt, ct);

    // Company-wide: el job corre sin sede, así que el filtro por defecto no aplica. El límite es el CompanyId.
    public async Task<bool> WasSentAsync(
        Guid companyId, Guid chargeId, int stageDaysOverdue, CancellationToken ct = default) =>
        await _db.NotificationLogs
            .IgnoreQueryFilters()
            .AnyAsync(n =>
                n.CompanyId == companyId &&
                n.ChargeId == chargeId &&
                n.StageDaysOverdue == stageDaysOverdue, ct);
}
