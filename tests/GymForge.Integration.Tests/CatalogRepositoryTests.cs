using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Regresión: SQLite no soporta ORDER BY sobre decimal. GetByCompany ordena por
/// Price y debe resolver en cliente sin lanzar.
/// </summary>
public class CatalogRepositoryTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new GymForgeDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task GetMembershipTypes_OrdersByPrice_WithoutSqliteError()
    {
        var companyId = Guid.NewGuid();
        await _db.MembershipTypes.AddRangeAsync(
            MembershipType.Create(companyId, "Anual", MembershipBasis.Renewal, 320_000m, 12, "Month"),
            MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month"),
            MembershipType.Create(companyId, "Trimestral", MembershipBasis.Renewal, 90_000m, 3, "Month"));
        await _db.SaveChangesAsync();

        var result = await new MembershipTypeRepository(_db).GetByCompanyAsync(companyId);

        result.Should().HaveCount(3);
        result.Select(m => m.Price).Should().BeInAscendingOrder();
    }
}
