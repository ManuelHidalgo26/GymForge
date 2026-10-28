using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Aislamiento multi-tenant por query filters globales (ADR-002). Mundo de prueba: la empresa A
/// tiene dos sedes (A1, A2) y la empresa B una (B1), cada una con sus socios, cargos y membresías.
/// </summary>
public class TenantFilterIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly AsOf = new(2026, 6, 1);

    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;

    private Company _companyA = null!, _companyB = null!;
    private Site _siteA1 = null!, _siteA2 = null!, _siteB1 = null!;
    private Member _memberA1 = null!, _memberA2 = null!, _memberB1 = null!;
    private Charge _chargeA1 = null!, _chargeA2 = null!, _chargeB1 = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);

        _companyA = Company.Create("Gym A", "30-11111111-1");
        _companyB = Company.Create("Gym B", "30-22222222-2");
        _siteA1 = Site.Create(_companyA.Id, "A Centro", "Calle 1");
        _siteA2 = Site.Create(_companyA.Id, "A Norte", "Calle 2");
        _siteB1 = Site.Create(_companyB.Id, "B Centro", "Calle 3");
        await _db.Companies.AddRangeAsync(_companyA, _companyB);
        await _db.Sites.AddRangeAsync(_siteA1, _siteA2, _siteB1);

        _memberA1 = MemberAt(_companyA, _siteA1, "Ana", "40000001");
        _memberA2 = MemberAt(_companyA, _siteA2, "Beto", "40000002");
        _memberB1 = MemberAt(_companyB, _siteB1, "Carla", "40000003");
        await _db.Members.AddRangeAsync(_memberA1, _memberA2, _memberB1);

        _chargeA1 = OverdueChargeFor(_memberA1);
        _chargeA2 = OverdueChargeFor(_memberA2);
        _chargeB1 = OverdueChargeFor(_memberB1);
        await _db.Charges.AddRangeAsync(_chargeA1, _chargeA2, _chargeB1);

        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private static Member MemberAt(Company company, Site site, string firstName, string dni) =>
        Member.Create(company.Id, site.Id, firstName, "Test", DocumentType.DNI, dni, Gender.Female);

    private static Charge OverdueChargeFor(Member member) =>
        Charge.Create(member.CompanyId, member.SiteId, member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, AsOf.AddDays(-10));

    // ── Mecanismo del filtro ──────────────────────────────────────────────────

    [Fact]
    public async Task SiteScopedEntities_AreVisibleOnlyInTheirOwnSite()
    {
        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await _db.Members.Select(m => m.Id).ToListAsync()).Should().BeEquivalentTo([_memberA1.Id]);

        using (_tenant.BeginScope(_companyA.Id, _siteA2.Id))
            (await _db.Members.Select(m => m.Id).ToListAsync()).Should().BeEquivalentTo([_memberA2.Id]);
    }

    [Fact]
    public async Task CompanyScopedEntities_AreSharedBetweenSitesButNotBetweenCompanies()
    {
        var planA = MembershipType.Create(_companyA.Id, "Mensual A", MembershipBasis.Renewal, 35_000m);
        var planB = MembershipType.Create(_companyB.Id, "Mensual B", MembershipBasis.Renewal, 35_000m);
        await _db.MembershipTypes.AddRangeAsync(planA, planB);
        await _db.SaveChangesAsync();

        foreach (var site in new[] { _siteA1, _siteA2 })
            using (_tenant.BeginScope(_companyA.Id, site.Id))
                (await _db.MembershipTypes.Select(p => p.Id).ToListAsync()).Should().BeEquivalentTo([planA.Id]);
    }

    [Fact]
    public async Task Exercises_ShowTheGlobalLibraryAndTheTenantsOwnOnly()
    {
        var global = Exercise.Create("Sentadilla", MuscleGroup.Quads, Equipment.Barbell, MovementType.Compound);
        var ownA = Exercise.Create("Propio A", MuscleGroup.Chest, Equipment.Dumbbell, MovementType.Isolation,
            tenantId: _companyA.Id);
        var ownB = Exercise.Create("Propio B", MuscleGroup.Chest, Equipment.Dumbbell, MovementType.Isolation,
            tenantId: _companyB.Id);
        await _db.Exercises.AddRangeAsync(global, ownA, ownB);
        await _db.SaveChangesAsync();

        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await _db.Exercises.Select(e => e.Id).ToListAsync())
                .Should().BeEquivalentTo([global.Id, ownA.Id]);
    }

    [Fact]
    public async Task Companies_AreNotFiltered_SoTheTenantCanBeResolvedAtBootstrap()
    {
        (await _db.Companies.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task WithoutATenant_SiteScopedEntitiesReturnNothing()
    {
        (await _db.Members.CountAsync()).Should().Be(0);
        (await _db.Charges.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task IgnoreQueryFilters_BypassesTheFilterExplicitly()
    {
        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await _db.Members.IgnoreQueryFilters().CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task Tenant_IsReadAtQueryTime_NotFixedWhenTheModelIsBuilt()
    {
        _tenant.Set(_companyA.Id, _siteA1.Id);
        (await _db.Members.Select(m => m.Id).SingleAsync()).Should().Be(_memberA1.Id);

        _tenant.Set(_companyB.Id, _siteB1.Id);
        (await _db.Members.Select(m => m.Id).SingleAsync()).Should().Be(_memberB1.Id);
    }

    [Fact]
    public async Task EachContext_FiltersByItsOwnTenant_EvenThoughTheModelIsShared()
    {
        // Segundo contexto sobre la MISMA base (misma conexión) pero con su propio provider.
        var otherTenant = new AmbientTenantProvider();
        var options = new DbContextOptionsBuilder<GymForgeDbContext>()
            .UseSqlite(_db.Database.GetDbConnection())
            .Options;
        await using var otherDb = new GymForgeDbContext(options, otherTenant);

        _tenant.Set(_companyA.Id, _siteA1.Id);
        otherTenant.Set(_companyB.Id, _siteB1.Id);

        (await _db.Members.Select(m => m.CompanyId).SingleAsync()).Should().Be(_companyA.Id);
        (await otherDb.Members.Select(m => m.CompanyId).SingleAsync()).Should().Be(_companyB.Id);
    }

    // ── Repositorios ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ChargeRepository_GetById_RespectsTheTenantFilter()
    {
        var repo = new ChargeRepository(_db);

        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
        {
            (await repo.GetByIdAsync(_chargeA1.Id)).Should().NotBeNull();
            (await repo.GetByIdAsync(_chargeA2.Id)).Should().BeNull("el cargo es de otra sede");
            (await repo.GetByIdAsync(_chargeB1.Id)).Should().BeNull("el cargo es de otra empresa");
        }
    }

    [Fact]
    public async Task SiteRepository_ResolvesTheTenantBeforeOneIsSet()
    {
        var repo = new SiteRepository(_db);   // sin tenant: así corre SessionContext.InitializeAsync

        (await repo.GetCompaniesAsync()).Should().HaveCount(2);
        (await repo.GetByCompanyAsync(_companyA.Id)).Select(s => s.Id)
            .Should().BeEquivalentTo([_siteA1.Id, _siteA2.Id]);
        (await repo.GetCompanyAsync(_companyB.Id)).Should().NotBeNull();
        (await repo.GetSiteAsync(_siteB1.Id)).Should().NotBeNull();
        (await repo.CountActiveSitesAsync(_companyA.Id)).Should().Be(2);
    }

    [Fact]
    public async Task StaffRepository_ListsTheCompanysStaffBeforeATenantIsSet()
    {
        var staffA = Staff.Create(_companyA.Id, "Ana", "Admin", StaffRole.Admin, "hash");
        var staffB = Staff.Create(_companyB.Id, "Beto", "Admin", StaffRole.Admin, "hash");
        await _db.Staff.AddRangeAsync(staffA, staffB);
        await _db.SaveChangesAsync();

        var result = await new StaffRepository(_db).GetActiveByCompanyAsync(_companyA.Id);

        result.Select(s => s.Id).Should().BeEquivalentTo([staffA.Id]);
    }

    [Fact]
    public async Task MemberRepository_CountByCompany_CountsEverySiteOfTheCompany()
    {
        // El límite de licencia es por empresa: no se puede esquivar creando socios en otra sede.
        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await new MemberRepository(_db).CountByCompanyAsync(_companyA.Id)).Should().Be(2);
    }

    [Fact]
    public async Task SiteRepository_SiteHasData_SeesOtherSitesThanTheActiveOne()
    {
        // Antes de borrar una sede hay que ver sus datos aunque no sea la sede activa.
        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
            (await new SiteRepository(_db).SiteHasDataAsync(_siteA2.Id)).Should().BeTrue();
    }

    [Fact]
    public async Task ChargeRepository_GetOverdue_IsCompanyWide_ButNeverCrossesCompanies()
    {
        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
        {
            var overdue = await new ChargeRepository(_db).GetOverdueAsync(_companyA.Id, AsOf);

            overdue.Select(c => c.Id).Should().BeEquivalentTo([_chargeA1.Id, _chargeA2.Id]);
        }
    }

    [Fact]
    public async Task MembershipRepository_GetExpiring_IsCompanyWide_ButNeverCrossesCompanies()
    {
        var plan = MembershipType.Create(_companyA.Id, "Mensual", MembershipBasis.Renewal, 35_000m);
        var planB = MembershipType.Create(_companyB.Id, "Mensual", MembershipBasis.Renewal, 35_000m);
        await _db.MembershipTypes.AddRangeAsync(plan, planB);
        var expiring = new[]
        {
            ActiveMembership(_memberA1, plan),
            ActiveMembership(_memberA2, plan),
            ActiveMembership(_memberB1, planB),
        };
        await _db.Memberships.AddRangeAsync(expiring);
        await _db.SaveChangesAsync();

        using (_tenant.BeginScope(_companyA.Id, _siteA1.Id))
        {
            var result = await new MembershipRepository(_db)
                .GetExpiringAsync(_companyA.Id, AsOf, AsOf.AddDays(7));

            result.Select(m => m.MemberId).Should().BeEquivalentTo([_memberA1.Id, _memberA2.Id]);
        }
    }

    private static Membership ActiveMembership(Member member, MembershipType plan)
    {
        var membership = Membership.Create(
            member.CompanyId, member.SiteId, member.Id, plan.Id, AsOf.AddDays(-20), AsOf.AddDays(3));
        membership.Activate();
        return membership;
    }
}
