using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class MemberRepository : IMemberRepository
{
    private readonly GymForgeDbContext _db;
    public MemberRepository(GymForgeDbContext db) => _db = db;

    public async Task<Member?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Members
            .Include(m => m.Memberships).ThenInclude(ms => ms.MembershipType)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<Member>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Members.ToListAsync(ct);

    public async Task AddAsync(Member entity, CancellationToken ct = default) =>
        await _db.Members.AddAsync(entity, ct);

    public void Update(Member entity) => _db.Members.Update(entity);
    public void Remove(Member entity) => _db.Members.Remove(entity);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) =>
        await _db.SaveChangesAsync(ct);

    public async Task<Member?> FindByDocumentAsync(
        DocumentType docType, string docNumber, Guid companyId, CancellationToken ct = default) =>
        await _db.Members
            .FirstOrDefaultAsync(m =>
                m.CompanyId == companyId &&
                m.DocumentType == docType &&
                m.DocumentNumber == docNumber, ct);

    public async Task<Member?> FindByTagSerialAsync(string tagSerial, Guid companyId, CancellationToken ct = default) =>
        await _db.Members
            .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.TagSerial == tagSerial, ct);

    public async Task<Member?> FindByEmailAsync(string email, Guid companyId, CancellationToken ct = default) =>
        await _db.Members
            .FirstOrDefaultAsync(m => m.CompanyId == companyId && m.Email == email, ct);

    public async Task<IReadOnlyList<Member>> SearchAsync(
        string query, Guid companyId, Guid siteId, int take = 50, CancellationToken ct = default)
    {
        var lower = query.ToLowerInvariant();
        return await _db.Members
            .Where(m =>
                m.CompanyId == companyId &&
                m.SiteId == siteId &&
                (m.FirstName.ToLower().Contains(lower) ||
                 m.LastName.ToLower().Contains(lower) ||
                 m.DocumentNumber.Contains(lower) ||
                 (m.Email != null && m.Email.ToLower().Contains(lower)) ||
                 (m.Mobile != null && m.Mobile.Contains(lower))))
            .OrderBy(m => m.LastName).ThenBy(m => m.FirstName)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Member>> GetPagedAsync(
        Guid companyId, Guid siteId, int page, int pageSize,
        MemberStatus? status = null, CancellationToken ct = default)
    {
        var query = _db.Members
            .Where(m => m.CompanyId == companyId && m.SiteId == siteId);

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        return await query
            .OrderBy(m => m.LastName).ThenBy(m => m.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<bool> HasActivityAsync(Guid memberId, CancellationToken ct = default) =>
        await _db.Memberships.AnyAsync(m => m.MemberId == memberId, ct) ||
        await _db.Charges.AnyAsync(c => c.MemberId == memberId, ct) ||
        await _db.Payments.AnyAsync(p => p.MemberId == memberId, ct) ||
        await _db.AccessLogs.AnyAsync(a => a.MemberId == memberId, ct);

    public async Task<int> CountAsync(
        Guid companyId, Guid siteId, MemberStatus? status = null, CancellationToken ct = default)
    {
        var query = _db.Members.Where(m => m.CompanyId == companyId && m.SiteId == siteId);
        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);
        return await query.CountAsync(ct);
    }
}
