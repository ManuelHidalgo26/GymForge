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

    public async Task<IReadOnlyList<Product>> GetAllByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Products
            .Include(p => p.Stock)
            .Where(p => p.CompanyId == companyId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

    public async Task<Product?> GetBySkuAsync(Guid companyId, string sku, CancellationToken ct = default) =>
        await _db.Products.FirstOrDefaultAsync(
            p => p.CompanyId == companyId && p.Sku == sku, ct);

    public async Task<StockBySite?> GetStockAsync(Guid productId, Guid siteId, CancellationToken ct = default) =>
        await _db.StockBySite.FirstOrDefaultAsync(s => s.ProductId == productId && s.SiteId == siteId, ct);

    public async Task AddAsync(Product product, CancellationToken ct = default) =>
        await _db.Products.AddAsync(product, ct);

    public async Task AddStockAsync(StockBySite stock, CancellationToken ct = default) =>
        await _db.StockBySite.AddAsync(stock, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        _db.SaveChangesAsync(ct);
}
