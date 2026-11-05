using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
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
    private AmbientTenantProvider _tenant = null!;
    private MemberRepository _repo = null!;
    private Guid _companyId;
    private Site _site = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
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
        _tenant.Set(company.Id, site.Id);
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

        // Cada tenant resuelve su propio socio aunque el tag colisione: el scope fija cuál es el activo.
        Member? fromTenant1, fromTenant2;
        using (_tenant.BeginScope(_companyId, _site.Id))
            fromTenant1 = await _repo.FindByTagSerialAsync("SHARED-TAG", _companyId);
        using (_tenant.BeginScope(otherCompany.Id, otherSite.Id))
            fromTenant2 = await _repo.FindByTagSerialAsync("SHARED-TAG", otherCompany.Id);

        fromTenant1!.LastName.Should().Be("Uno");
        fromTenant2!.LastName.Should().Be("Dos");

        // El listado del tenant 1 nunca incluye socios de otro tenant.
        var page = await _repo.GetPagedAsync(_companyId, _site.Id, 1, 50);
        page.Should().OnlyContain(m => m.CompanyId == _companyId);
    }

    // ── Credencial única por empresa ──────────────────────────────────────────

    private Member NewMember(string firstName, string dni, string? tag = null, Site? site = null, Guid? companyId = null)
    {
        var member = Member.Create(companyId ?? _companyId, (site ?? _site).Id, firstName, "Test",
            DocumentType.DNI, dni, Gender.Female);
        if (tag is not null) member.EnrollTag(tag);
        return member;
    }

    private async Task<Site> AddSecondSiteAsync()
    {
        var site = Site.Create(_companyId, "Sede Norte", "Av. Test 456");
        await _db.Sites.AddAsync(site);
        await _db.SaveChangesAsync();
        return site;
    }

    [Fact]
    public async Task TagSerial_RepeatedInTheSameCompany_IsRejectedByTheUniqueIndex_EvenAcrossSites()
    {
        var otherSite = await AddSecondSiteAsync();
        await _repo.AddAsync(NewMember("Ana", "50000001", tag: "CARNET-1"));
        await _repo.AddAsync(NewMember("Beto", "50000002", tag: "CARNET-1", site: otherSite));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task TagSerial_RepeatedInAnotherCompany_IsAllowed()
    {
        var otherCompany = Company.Create("Otra Empresa", "30-99999999-7");
        var otherSite = Site.Create(otherCompany.Id, "Otra Sede", "Calle 2");
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _repo.AddAsync(NewMember("Ana", "50000001", tag: "CARNET-1"));
        await _db.Members.AddAsync(NewMember("Beto", "50000002", tag: "CARNET-1", site: otherSite, companyId: otherCompany.Id));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TagSerial_NullOrEmpty_IsNotSubjectToTheUniqueIndex()
    {
        await _repo.AddAsync(NewMember("Sin1", "50000001"));
        await _repo.AddAsync(NewMember("Sin2", "50000002"));
        await _repo.AddAsync(NewMember("Vacio1", "50000003", tag: ""));
        await _repo.AddAsync(NewMember("Vacio2", "50000004", tag: ""));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Document_RepeatedInTheSameCompanyAndSite_IsRejectedWithAReadableError()
    {
        await _repo.AddAsync(NewMember("Ana", "50000001"));
        await _repo.SaveChangesAsync();
        await _repo.AddAsync(NewMember("Beto", "50000001"));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DuplicateMemberDocumentException>().WithMessage("*DNI*sede*");
    }

    [Fact]
    public async Task Document_RepeatedInAnotherSiteOfTheSameCompany_IsAllowed()
    {
        var otherSite = await AddSecondSiteAsync();
        await _repo.AddAsync(NewMember("Ana", "50000001"));
        await _repo.AddAsync(NewMember("Beto", "50000001", site: otherSite));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Document_RepeatedInAnotherCompany_IsAllowed()
    {
        var otherCompany = Company.Create("Otra Empresa", "30-99999999-7");
        var otherSite = Site.Create(otherCompany.Id, "Otra Sede", "Calle 2");
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _repo.AddAsync(NewMember("Ana", "50000001"));
        await _db.Members.AddAsync(NewMember("Beto", "50000001", site: otherSite, companyId: otherCompany.Id));

        var act = () => _repo.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Document_ChangedToOneAlreadyInTheSite_IsRejectedWithAReadableError()
    {
        await _repo.AddAsync(NewMember("Ana", "50000001"));
        var beto = NewMember("Beto", "50000002");
        await _repo.AddAsync(beto);
        await _repo.SaveChangesAsync();
        beto.EditIdentity("Beto", "Test", DocumentType.DNI, "50000001", Gender.Female);

        var act = () => _repo.SaveChangesAsync();

        await act.Should().ThrowAsync<DuplicateMemberDocumentException>();
    }

    [Fact]
    public async Task IsTagSerialInUse_SeesAnotherSiteOfTheCompany_ButNeverAnotherCompany()
    {
        var otherSite = await AddSecondSiteAsync();
        var otherCompany = Company.Create("Otra Empresa", "30-99999999-7");
        var foreignSite = Site.Create(otherCompany.Id, "Otra Sede", "Calle 2");
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(foreignSite);
        await _repo.AddAsync(NewMember("Ana", "50000001", tag: "EN-OTRA-SEDE", site: otherSite));
        await _db.Members.AddAsync(NewMember("Beto", "50000002", tag: "DE-OTRA-EMPRESA", site: foreignSite, companyId: otherCompany.Id));
        await _db.SaveChangesAsync();

        (await _repo.IsTagSerialInUseAsync(_companyId, "EN-OTRA-SEDE")).Should().BeTrue("la unicidad es por empresa, no por sede");
        (await _repo.IsTagSerialInUseAsync(_companyId, "DE-OTRA-EMPRESA")).Should().BeFalse();
        (await _repo.IsTagSerialInUseAsync(_companyId, "NO-EXISTE")).Should().BeFalse();
    }

    [Fact]
    public async Task IsTagSerialInUse_CanExcludeTheMemberItself()
    {
        var ana = NewMember("Ana", "50000001", tag: "CARNET-1");
        await _repo.AddAsync(ana);
        await _repo.SaveChangesAsync();

        (await _repo.IsTagSerialInUseAsync(_companyId, "CARNET-1", exceptMemberId: ana.Id)).Should().BeFalse();
        (await _repo.IsTagSerialInUseAsync(_companyId, "CARNET-1", exceptMemberId: Guid.NewGuid())).Should().BeTrue();
        (await _repo.IsTagSerialInUseAsync(_companyId, "CARNET-1")).Should().BeTrue();
    }

    [Fact]
    public async Task AssignCredential_OfACredentialAlreadyTaken_IsRejectedBeforeReachingTheIndex()
    {
        var ana = NewMember("Ana", "50000001", tag: "CARNET-1");
        var beto = NewMember("Beto", "50000002");
        await _repo.AddAsync(ana);
        await _repo.AddAsync(beto);
        await _repo.SaveChangesAsync();
        var handler = new AssignCredentialCommandHandler(_repo);

        var act = () => handler.Handle(new AssignCredentialCommand(_companyId, beto.Id, "CARNET-1"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*otro socio*");
        _db.ChangeTracker.Clear();
        (await _db.Members.AsNoTracking().SingleAsync(m => m.Id == beto.Id)).TagSerial.Should().BeNull();
    }

    [Fact]
    public async Task GlobalFilter_ShowsOnlyTheActiveTenant_WithoutAnExplicitWhere()
    {
        await _repo.AddAsync(Member.Create(_companyId, _site.Id, "Tenant", "Uno",
            DocumentType.DNI, "40000001", Gender.Male));
        var otherCompany = Company.Create("Otra Empresa", "30-99999999-7");
        var otherSite = Site.Create(otherCompany.Id, "Otra Sede", "Calle 2");
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _db.Members.AddAsync(Member.Create(otherCompany.Id, otherSite.Id, "Tenant", "Dos",
            DocumentType.DNI, "40000002", Gender.Male));
        await _db.SaveChangesAsync();

        using (_tenant.BeginScope(_companyId, _site.Id))
            (await _db.Members.ToListAsync()).Should().ContainSingle().Which.CompanyId.Should().Be(_companyId);
    }
}
