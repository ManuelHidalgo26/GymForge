using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class ChargeRepository : IChargeRepository
{
    private readonly GymForgeDbContext _db;
    public ChargeRepository(GymForgeDbContext db) => _db = db;

    public async Task<Charge?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Charges.FindAsync([id], ct);

    public async Task<IReadOnlyList<Charge>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Charges.ToListAsync(ct);

    public async Task AddAsync(Charge entity, CancellationToken ct = default) =>
        await _db.Charges.AddAsync(entity, ct);

    public void Update(Charge entity) => _db.Charges.Update(entity);
    public void Remove(Charge entity) => _db.Charges.Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);

    public async Task<decimal> SumOutstandingAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Charges
            .Where(c =>
                c.MemberId == memberId &&
                (c.Status == ChargeStatus.Pending ||
                 c.Status == ChargeStatus.PartiallyPaid ||
                 c.Status == ChargeStatus.Overdue))
            .SumAsync(c => c.Amount + c.TaxAmount - c.AmountPaid, ct);

    public async Task<IReadOnlyList<Charge>> GetPendingAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Charges
            .Where(c =>
                c.MemberId == memberId &&
                c.Status != ChargeStatus.Paid &&
                c.Status != ChargeStatus.WrittenOff &&
                c.Status != ChargeStatus.Refunded)
            .OrderBy(c => c.DueDate)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Charge>> GetOverdueAsync(
        Guid companyId, DateOnly asOf, CancellationToken ct = default) =>
        await _db.Charges
            .Include(c => c.Member)
            .Where(c =>
                c.CompanyId == companyId &&
                c.DueDate < asOf &&
                c.Status != ChargeStatus.Paid &&
                c.Status != ChargeStatus.WrittenOff)
            .OrderBy(c => c.DueDate)
            .ToListAsync(ct);
}
