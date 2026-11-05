using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class MembershipRepository : IMembershipRepository
{
    private readonly GymForgeDbContext _db;
    public MembershipRepository(GymForgeDbContext db) => _db = db;

    public async Task<Membership?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.MembershipType)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<Membership>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Memberships.ToListAsync(ct);

    public async Task AddAsync(Membership entity, CancellationToken ct = default) =>
        await _db.Memberships.AddAsync(entity, ct);

    public void Update(Membership entity) => _db.Memberships.Update(entity);
    public void Remove(Membership entity) => _db.Memberships.Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);

    public async Task<Membership?> GetCurrentActiveAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Memberships
            .Include(m => m.MembershipType)
            .Where(m => m.MemberId == memberId && m.Status == MembershipStatus.Active)
            .OrderByDescending(m => m.StartDate)
            .FirstOrDefaultAsync(ct);

    // Lecturas de pantalla y reportes (solo mostrar): sin tracking, así no quedan instancias viejas en el contexto de
    // larga vida de Desktop. Las que alimentan una escritura (GetByIdAsync, GetCurrentActiveAsync, los jobs) siguen trackeadas.
    public async Task<IReadOnlyList<Membership>> GetByMemberAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Memberships
            .AsNoTracking()
            .Include(m => m.MembershipType)
            .Where(m => m.MemberId == memberId)
            .OrderByDescending(m => m.StartDate)
            .ToListAsync(ct);

    // Vencimientos de la empresa completa (todas las sedes); el límite de tenant es el CompanyId explícito.
    public async Task<IReadOnlyList<Membership>> GetExpiringAsync(
        Guid companyId, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        await _db.Memberships
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(m => m.Member)
            .Include(m => m.MembershipType)
            .Where(m =>
                m.CompanyId == companyId &&
                m.Status == MembershipStatus.Active &&
                m.EndDate >= from &&
                m.EndDate <= to)
            .OrderBy(m => m.EndDate)
            .ToListAsync(ct);

    // Vencidas de la empresa completa (todas las sedes); el límite de tenant es el CompanyId explícito.
    public async Task<IReadOnlyList<Membership>> GetActiveExpiredAsync(
        Guid companyId, DateOnly asOf, CancellationToken ct = default) =>
        await _db.Memberships
            .IgnoreQueryFilters()
            .Where(m =>
                m.CompanyId == companyId &&
                m.Status == MembershipStatus.Active &&
                m.EndDate != null &&
                m.EndDate < asOf)
            .ToListAsync(ct);

    // Congelamientos terminados de la empresa completa (todas las sedes); el límite de tenant es el CompanyId explícito.
    public async Task<IReadOnlyList<Membership>> GetFreezeEndedAsync(
        Guid companyId, DateOnly asOf, CancellationToken ct = default) =>
        await _db.Memberships
            .IgnoreQueryFilters()
            .Where(m =>
                m.CompanyId == companyId &&
                m.Status == MembershipStatus.Frozen &&
                m.FreezeEnd != null &&
                m.FreezeEnd <= asOf)
            .ToListAsync(ct);

    // Reporte de dueño: IgnoreQueryFilters solo abre la sede; el CompanyId explícito mantiene el límite de empresa.
    public async Task<IReadOnlyList<Membership>> GetCancelledBetweenAsync(
        Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.Memberships
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Include(m => m.Member)
            .Include(m => m.MembershipType)
            .Where(m => m.CompanyId == companyId && (siteId == null || m.SiteId == siteId) &&
                        m.CancelDate >= from && m.CancelDate < to)
            .ToListAsync(ct);

    // Reporte de comisiones: mismo patrón que GetCancelledBetweenAsync (sede abierta, empresa acotada).
    public async Task<IReadOnlyList<Membership>> GetSoldBetweenAsync(
        Guid companyId, Guid? siteId, DateTime from, DateTime to, CancellationToken ct = default) =>
        await _db.Memberships
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(m => m.CompanyId == companyId && (siteId == null || m.SiteId == siteId) &&
                        m.SoldByStaffId != null && m.CommissionAmount > 0 &&
                        m.CreatedAt >= from && m.CreatedAt < to)
            .ToListAsync(ct);
}
