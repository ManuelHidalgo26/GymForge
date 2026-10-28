using FluentAssertions;
using GymForge.Application.UseCases.Memberships;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>Tope de freeze por empresa y cupo anual por membresía, contra SQLite real (ADR-003 punto 3).</summary>
public class FreezePolicyIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _site = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Company_DefaultPolicy_PersistsAs90()
    {
        _db.ChangeTracker.Clear();

        var reloaded = await _db.Companies.SingleAsync();

        reloaded.MaxFreezeDaysPerYear.Should().Be(90);
    }

    [Fact]
    public async Task Company_ExplicitZeroCap_IsNotReplacedByTheColumnDefault()
    {
        var strict = Company.Create("Gym Sin Freeze", "30-98765432-1");
        strict.SetFreezePolicy(0);
        await _db.Companies.AddAsync(strict);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reloaded = await _db.Companies.SingleAsync(c => c.Id == strict.Id);

        reloaded.MaxFreezeDaysPerYear.Should().Be(0);
    }

    [Fact]
    public async Task FreezeThenUnfreeze_PersistsQuotaAndEndDate()
    {
        var ms = await AddActiveMembershipAsync();
        var repo = new MembershipRepository(_db);
        var sites = new SiteRepository(_db);

        await new FreezeMembershipCommandHandler(repo, sites).Handle(
            new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje"),
            CancellationToken.None);
        _db.ChangeTracker.Clear();

        var frozen = await _db.Memberships.SingleAsync();
        frozen.Status.Should().Be(MembershipStatus.Frozen);
        frozen.FreezeYear.Should().Be(2026);
        frozen.FreezeDaysUsedYear.Should().Be(30);

        await UnfreezeAsync(ms.Id, FakeClock.OnDay(new DateOnly(2026, 3, 11)));
        _db.ChangeTracker.Clear();

        var active = await _db.Memberships.SingleAsync();
        active.Status.Should().Be(MembershipStatus.Active);
        active.FreezeDaysUsedYear.Should().Be(10);
        active.EndDate.Should().Be(new DateOnly(2027, 1, 10));
    }

    [Fact]
    public async Task Unfreeze_At2200Argentina_GivesBackTheSameDaysAsDuringTheDay()
    {
        var ms = await AddActiveMembershipAsync();
        await new FreezeMembershipCommandHandler(new MembershipRepository(_db), new SiteRepository(_db)).Handle(
            new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje"),
            CancellationToken.None);
        _db.ChangeTracker.Clear();

        // 22:00 del 11/03 en Argentina = 01:00 UTC del 12/03: con la fecha UTC el fin quedaba un día corrido.
        await UnfreezeAsync(ms.Id, FakeClock.At2200Argentina(new DateOnly(2026, 3, 11)));
        _db.ChangeTracker.Clear();

        var active = await _db.Memberships.SingleAsync();
        active.FreezeDaysUsedYear.Should().Be(10);
        active.EndDate.Should().Be(new DateOnly(2027, 1, 10));
    }

    private Task UnfreezeAsync(Guid membershipId, FakeClock clock) =>
        new UnfreezeMembershipCommandHandler(new MembershipRepository(_db), TestServices.Calendar(_db, clock))
            .Handle(new UnfreezeMembershipCommand(membershipId), CancellationToken.None);

    [Fact]
    public async Task Freeze_BeyondTheCompanyCap_IsRejectedAndNothingChanges()
    {
        _company.SetFreezePolicy(15);
        await _db.SaveChangesAsync();
        var ms = await AddActiveMembershipAsync();

        var act = () => new FreezeMembershipCommandHandler(new MembershipRepository(_db), new SiteRepository(_db)).Handle(
            new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _db.ChangeTracker.Clear();
        (await _db.Memberships.SingleAsync()).Status.Should().Be(MembershipStatus.Active);
    }

    private async Task<Membership> AddActiveMembershipAsync()
    {
        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        var plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 12, "Month");
        var ms = Membership.Create(
            _company.Id, _site.Id, member.Id, plan.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        ms.Activate();
        await _db.Members.AddAsync(member);
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(ms);
        await _db.SaveChangesAsync();
        return ms;
    }
}
