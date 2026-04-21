using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Integration tests against an in-memory SQLite DB.
/// For full PostgreSQL integration, use Testcontainers (Sprint 2).
/// </summary>
public class MemberRepositoryIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private MemberRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new GymForgeDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _repo = new MemberRepository(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task AddAndRetrieveMember_RoundTrip()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        // Need a Site first (FK)
        var site = Site.Create(companyId, "Test Site", "Av. Test 123");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        var member = Member.Create(companyId, site.Id, "Ana", "García",
            DocumentType.DNI, "30123456", Gender.Female);
        member.UpdateContact("ana@test.com", "+5491123456789");

        await _repo.AddAsync(member);
        await _repo.SaveChangesAsync();

        var loaded = await _repo.GetByIdAsync(member.Id);

        loaded.Should().NotBeNull();
        loaded!.FullName.Should().Be("Ana García");
        loaded.Email.Should().Be("ana@test.com");
        loaded.DocumentNumber.Should().Be("30123456");
    }

    [Fact]
    public async Task FindByTagSerial_ReturnsCorrectMember()
    {
        var companyId = Guid.NewGuid();
        var site = Site.Create(companyId, "Site", "Addr");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        var m1 = Member.Create(companyId, site.Id, "Carlos", "López", DocumentType.DNI, "22111222", Gender.Male);
        m1.EnrollTag("CARD-ABC123");
        var m2 = Member.Create(companyId, site.Id, "María", "Torres", DocumentType.DNI, "33222333", Gender.Female);
        m2.EnrollTag("CARD-XYZ999");

        await _repo.AddAsync(m1);
        await _repo.AddAsync(m2);
        await _repo.SaveChangesAsync();

        var found = await _repo.FindByTagSerialAsync("CARD-ABC123", companyId);

        found.Should().NotBeNull();
        found!.FirstName.Should().Be("Carlos");
    }

    [Fact]
    public async Task SearchMembers_ByLastName_ReturnsMatches()
    {
        var companyId = Guid.NewGuid();
        var site = Site.Create(companyId, "Site", "Addr");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        var members = new[]
        {
            Member.Create(companyId, site.Id, "Pedro", "Ramírez", DocumentType.DNI, "11000001", Gender.Male),
            Member.Create(companyId, site.Id, "Laura", "Ramírez", DocumentType.DNI, "11000002", Gender.Female),
            Member.Create(companyId, site.Id, "Jorge", "Fernández", DocumentType.DNI, "11000003", Gender.Male),
        };

        foreach (var m in members) await _repo.AddAsync(m);
        await _repo.SaveChangesAsync();

        var results = await _repo.SearchAsync("Ramírez", companyId, site.Id);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(m => m.LastName == "Ramírez");
    }

    [Fact]
    public async Task GetPaged_WithStatusFilter_ReturnsCorrectSubset()
    {
        var companyId = Guid.NewGuid();
        var site = Site.Create(companyId, "Site", "Addr");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        for (int i = 0; i < 5; i++)
        {
            var m = Member.Create(companyId, site.Id, $"Activo{i}", "Test",
                DocumentType.DNI, $"200000{i:D2}", Gender.Male);
            m.Activate(DateOnly.FromDateTime(DateTime.Today));
            await _repo.AddAsync(m);
        }
        for (int i = 0; i < 3; i++)
        {
            var m = Member.Create(companyId, site.Id, $"Prospect{i}", "Test",
                DocumentType.DNI, $"300000{i:D2}", Gender.Female);
            await _repo.AddAsync(m);
        }
        await _repo.SaveChangesAsync();

        var activos = await _repo.GetPagedAsync(companyId, site.Id, 1, 50, MemberStatus.Active);
        var prospectos = await _repo.GetPagedAsync(companyId, site.Id, 1, 50, MemberStatus.Prospect);

        activos.Should().HaveCount(5);
        prospectos.Should().HaveCount(3);
    }
}
