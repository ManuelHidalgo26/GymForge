using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly GymForgeDbContext _db;
    public ProductRepository(GymForgeDbContext db) => _db = db;

    public async Task<Product?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Product>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Products
            .Where(p => p.CompanyId == companyId && p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<StockBySite?> GetStockAsync(Guid productId, Guid siteId, CancellationToken ct = default) =>
        await _db.StockBySite.FirstOrDefaultAsync(s => s.ProductId == productId && s.SiteId == siteId, ct);
}
