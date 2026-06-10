using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly GymForgeDbContext _db;
    public PaymentRepository(GymForgeDbContext db) => _db = db;

    public async Task AddAsync(Payment payment, CancellationToken ct = default) =>
        await _db.Payments.AddAsync(payment, ct);

    public async Task<decimal> SumReceivedAsync(
        Guid companyId, Guid siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.Payments
            .Where(p => p.CompanyId == companyId && p.SiteId == siteId &&
                        p.ReceivedAt >= from && p.ReceivedAt < to &&
                        p.Status == PaymentStatus.Completed)
            .SumAsync(p => p.Amount, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
