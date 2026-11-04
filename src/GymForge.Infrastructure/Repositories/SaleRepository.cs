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

    // Reporte de comisiones: la línea no tiene empresa ni sede propias, las toma de su venta.
    public async Task<IReadOnlyList<SaleLine>> GetCommissionLinesBetweenAsync(
        Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.SaleLines
            .IgnoreQueryFilters()
            .Include(l => l.Sale)
            .Where(l => l.Sale.CompanyId == companyId && (siteId == null || l.Sale.SiteId == siteId) &&
                        l.CommissionStaffId != null && l.CommissionAmount > 0 &&
                        l.Sale.SaleDatetime >= from && l.Sale.SaleDatetime < to)
            .ToListAsync(ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
