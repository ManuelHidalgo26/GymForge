using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class SaleRepository : ISaleRepository
{
    private readonly GymForgeDbContext _db;
    public SaleRepository(GymForgeDbContext db) => _db = db;

    public async Task AddAsync(Sale sale, CancellationToken ct = default) =>
        await _db.Sales.AddAsync(sale, ct);

    public async Task<Sale?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Sales
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

    public void UpdateStock(StockBySite stock) => _db.StockBySite.Update(stock);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
