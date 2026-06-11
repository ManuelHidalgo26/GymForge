using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class SiteRepository : ISiteRepository
{
    private readonly GymForgeDbContext _db;
    public SiteRepository(GymForgeDbContext db) => _db = db;

    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default) =>
        await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.LegalName).ToListAsync(ct);

    public async Task<IReadOnlyList<Site>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Sites
            .Where(s => s.CompanyId == companyId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<Company?> GetCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);

    public async Task AddSiteAsync(Site site, CancellationToken ct = default) =>
        await _db.Sites.AddAsync(site, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
