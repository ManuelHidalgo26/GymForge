using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Infrastructure.Repositories;

public class StaffRepository : IStaffRepository
{
    private readonly GymForgeDbContext _db;
    public StaffRepository(GymForgeDbContext db) => _db = db;

    public async Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        await _db.Staff.FirstOrDefaultAsync(s => s.Id == id, ct);

    public async Task<IReadOnlyList<Staff>> GetActiveByCompanyAsync(Guid companyId, CancellationToken ct = default) =>
        await _db.Staff
            .Where(s => s.CompanyId == companyId && s.IsActive)
            .ToListAsync(ct);

    public void Update(Staff staff) => _db.Staff.Update(staff);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default) => await _db.SaveChangesAsync(ct);
}
