using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

/// <summary>
/// Libro mayor de la cuenta corriente del socio. Append-only a propósito: no hay Update ni Remove.
/// Las lecturas pasan por el filtro global de tenant (empresa + sede).
/// </summary>
public class MemberCreditRepository : IMemberCreditRepository
{
    private readonly GymForgeDbContext _db;
    public MemberCreditRepository(GymForgeDbContext db) => _db = db;

    public async Task AddAsync(MemberCreditMovement movement, CancellationToken ct = default) =>
        await _db.MemberCreditMovements.AddAsync(movement, ct);

    public async Task<decimal> GetBalanceAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.MemberCreditMovements
            .Where(m => m.MemberId == memberId)
            .SumAsync(m => m.Amount, ct);

    public async Task<IReadOnlyList<MemberCreditMovement>> GetByMemberAsync(
        Guid memberId, CancellationToken ct = default) =>
        await _db.MemberCreditMovements
            .Where(m => m.MemberId == memberId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
