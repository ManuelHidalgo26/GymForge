using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class MembershipTypeRepository : IMembershipTypeRepository
{
    private readonly GymForgeDbContext _db;
    public MembershipTypeRepository(GymForgeDbContext db) => _db = db;

    public async Task<MembershipType?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.MembershipTypes.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlyList<MembershipType>> GetByCompanyAsync(Guid companyId, CancellationToken ct = default)
    {
        // SQLite no soporta ORDER BY sobre decimal → se ordena en cliente.
        var list = await _db.MembershipTypes
            .Where(m => m.CompanyId == companyId && m.IsActive)
            .ToListAsync(ct);
        return list.OrderBy(m => m.Price).ToList();
    }
}
