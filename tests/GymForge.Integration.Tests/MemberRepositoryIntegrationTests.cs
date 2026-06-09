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
    private Guid _companyId;
    private Site _site = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;

        _db = new GymForgeDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _repo = new MemberRepository(_db);

        // Tenant raíz: Company + Site. Member.SiteId → Site y Site.CompanyId → Company son FK
        // obligatorias, así que el padre debe existir antes de insertar cualquier Member.
        var company = Company.Create("Test Gym", "30-12345678-9");
        await _db.Companies.AddAsync(company);
        var site = Site.Create(company.Id, "Test Site", "Av. Test 123");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();

        _companyId = company.Id;
        _site = site;
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task AddAndRetrieveMember_RoundTrip()
    {
        var member = Member.Create(_companyId, _site.Id, "Ana", "García",
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
        var m1 = Member.Create(_companyId, _site.Id, "Carlos", "López", DocumentType.DNI, "22111222", Gender.Male);
        m1.EnrollTag("CARD-ABC123");
        var m2 = Member.Create(_companyId, _site.Id, "María", "Torres", DocumentType.DNI, "33222333", Gender.Female);
        m2.EnrollTag("CARD-XYZ999");

        await _repo.AddAsync(m1);
        await _repo.AddAsync(m2);
        await _repo.SaveChangesAsync();

        var found = await _repo.FindByTagSerialAsync("CARD-ABC123", _companyId);

        found.Should().NotBeNull();
        found!.FirstName.Should().Be("Carlos");
    }

    [Fact]
    public async Task SearchMembers_ByLastName_ReturnsMatches()
    {
        var members = new[]
        {
            Member.Create(_companyId, _site.Id, "Pedro", "Ramírez", DocumentType.DNI, "11000001", Gender.Male),
            Member.Create(_companyId, _site.Id, "Laura", "Ramírez", DocumentType.DNI, "11000002", Gender.Female),
            Member.Create(_companyId, _site.Id, "Jorge", "Fernández", DocumentType.DNI, "11000003", Gender.Male),
        };

        foreach (var m in members) await _repo.AddAsync(m);
        await _repo.SaveChangesAsync();

        var results = await _repo.SearchAsync("Ramírez", _companyId, _site.Id);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(m => m.LastName == "Ramírez");
    }

    [Fact]
    public async Task GetPaged_WithStatusFilter_ReturnsCorrectSubset()
    {
        for (int i = 0; i < 5; i++)
        {
            var m = Member.Create(_companyId, _site.Id, $"Activo{i}", "Test",
                DocumentType.DNI, $"200000{i:D2}", Gender.Male);
            m.Activate(DateOnly.FromDateTime(DateTime.Today));
            await _repo.AddAsync(m);
        }
        for (int i = 0; i < 3; i++)
        {
            var m = Member.Create(_companyId, _site.Id, $"Prospect{i}", "Test",
                DocumentType.DNI, $"300000{i:D2}", Gender.Female);
            await _repo.AddAsync(m);
        }
        await _repo.SaveChangesAsync();

        var activos = await _repo.GetPagedAsync(_companyId, _site.Id, 1, 50, MemberStatus.Active);
        var prospectos = await _repo.GetPagedAsync(_companyId, _site.Id, 1, 50, MemberStatus.Prospect);

        activos.Should().HaveCount(5);
        prospectos.Should().HaveCount(3);
    }

    [Fact]
    public async Task Queries_IsolateByTenant()
    {
        // Socio en el tenant sembrado, con un tag de credencial.
        var m1 = Member.Create(_companyId, _site.Id, "Tenant", "Uno",
            DocumentType.DNI, "40000001", Gender.Male);
        m1.EnrollTag("SHARED-TAG");
        await _repo.AddAsync(m1);

        // Segundo tenant que reusa exactamente el mismo tag.
        var otherCompany = Company.Create("Otra Empresa", "30-99999999-7");
        await _db.Companies.AddAsync(otherCompany);
        var otherSite = Site.Create(otherCompany.Id, "Otra Sede", "Calle 2");
        await _db.Sites.AddAsync(otherSite);
        var m2 = Member.Create(otherCompany.Id, otherSite.Id, "Tenant", "Dos",
            DocumentType.DNI, "40000002", Gender.Male);
        m2.EnrollTag("SHARED-TAG");
        await _db.Members.AddAsync(m2);
        await _db.SaveChangesAsync();

        // Cada tenant resuelve su propio socio aunque el tag colisione.
        var fromTenant1 = await _repo.FindByTagSerialAsync("SHARED-TAG", _companyId);
        var fromTenant2 = await _repo.FindByTagSerialAsync("SHARED-TAG", otherCompany.Id);

        fromTenant1!.LastName.Should().Be("Uno");
        fromTenant2!.LastName.Should().Be("Dos");

        // El listado del tenant 1 nunca incluye socios de otro tenant.
        var page = await _repo.GetPagedAsync(_companyId, _site.Id, 1, 50);
        page.Should().OnlyContain(m => m.CompanyId == _companyId);
    }
}
