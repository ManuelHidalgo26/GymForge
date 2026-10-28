using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

/// <summary>
/// Resolución del tenant (empresas y sedes): corre ANTES de que haya un tenant activo, y
/// SiteHasDataAsync inspecciona sedes que no son la activa. Por eso sus lecturas de entidades
/// filtradas usan IgnoreQueryFilters; el límite de empresa lo marcan los predicados explícitos.
/// </summary>
public class SiteRepository : ISiteRepository
{
    private readonly GymForgeDbContext _db;
    public SiteRepository(GymForgeDbContext db) => _db = db;

    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(CancellationToken ct = default) =>
        await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.LegalName).ToListAsync(ct);

    public async Task<IReadOnlyList<Site>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Sites
            .IgnoreQueryFilters()
            .Where(s => s.CompanyId == companyId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

    public async Task<Company?> GetCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Companies.FirstOrDefaultAsync(c => c.Id == companyId, ct);

    public async Task<Site?> GetSiteAsync(Guid siteId, CancellationToken ct = default) =>
        await _db.Sites.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.Id == siteId, ct);

    public void RemoveSite(Site site) => _db.Sites.Remove(site);

    public async Task<bool> SiteHasDataAsync(Guid siteId, CancellationToken ct = default) =>
        await _db.Members.IgnoreQueryFilters().AnyAsync(m => m.SiteId == siteId, ct) ||
        await _db.Shifts.IgnoreQueryFilters().AnyAsync(s => s.SiteId == siteId, ct) ||
        await _db.Payments.IgnoreQueryFilters().AnyAsync(p => p.SiteId == siteId, ct);

    public async Task<int> CountActiveSitesAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Sites.IgnoreQueryFilters().CountAsync(s => s.CompanyId == companyId && s.IsActive, ct);

    public async Task AddCompanyAsync(Company company, CancellationToken ct = default) =>
        await _db.Companies.AddAsync(company, ct);

    public async Task AddSiteAsync(Site site, CancellationToken ct = default) =>
        await _db.Sites.AddAsync(site, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);
}
