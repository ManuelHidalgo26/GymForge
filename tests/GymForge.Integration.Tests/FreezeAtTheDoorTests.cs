using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Maintenance;
using GymForge.Application.UseCases.Memberships;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>P-1 en la puerta: una membresía congelada no entra, y vuelve a entrar al reactivarla (SQLite real).</summary>
public class FreezeAtTheDoorTests : IAsyncLifetime
{
    private const string Tag = "TAG-FREEZE";
    private const int DoorId = 1;

    // El AccessLog se sella con el reloj real, así que el reloj de la prueba arranca en "ahora" (no en una fecha fija).
    private readonly FakeClock _clock = new(DateTime.UtcNow);
    private readonly DateOnly _today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Argentina Standard Time")));
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

    private async Task<Membership> SeedActiveMembershipAsync()
    {
        var plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30123456", Gender.Female);
        member.EnrollTag(Tag);
        var ms = Membership.Create(_company.Id, _site.Id, member.Id, plan.Id, _today.AddMonths(-1), _today.AddMonths(2));
        ms.Activate();
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Members.AddAsync(member);
        await _db.Memberships.AddAsync(ms);
        await _db.SaveChangesAsync();
        return ms;
    }

    private ValidateSwipeUseCase Gatekeeper() => new(
        new MemberRepository(_db), new MembershipRepository(_db), new ChargeRepository(_db),
        new AccessLogRepository(_db), Substitute.For<IEventBus>(), _clock,
        TestServices.Calendar(_db, _clock), _tenant, new GatekeeperConfig { Timezone = "UTC" });

    private Task<AccessDecision> SwipeAsync() => Gatekeeper().ValidateSwipeAsync(
        new ValidateSwipeRequest(Tag, AccessMethod.RfidCard, DoorId, _site.Id, _company.Id));

    private Task FreezeAsync(Guid membershipId) =>
        new FreezeMembershipCommandHandler(new MembershipRepository(_db), new SiteRepository(_db)).Handle(
            new FreezeMembershipCommand(membershipId, _today, _today.AddDays(10), "Viaje"), CancellationToken.None);

    private Task UnfreezeAsync(Guid membershipId) =>
        new UnfreezeMembershipCommandHandler(new MembershipRepository(_db), TestServices.Calendar(_db, _clock))
            .Handle(new UnfreezeMembershipCommand(membershipId), CancellationToken.None);

    [Fact]
    public async Task FrozenMembership_IsDeniedAtTheDoor_AsWithoutAnActiveMembership()
    {
        var ms = await SeedActiveMembershipAsync();
        await FreezeAsync(ms.Id);
        _db.ChangeTracker.Clear();

        var decision = await SwipeAsync();

        decision.Granted.Should().BeFalse();
        decision.DenialReason.Should().Be(AccessDenialReason.IncompleteMembership);
        _db.ChangeTracker.Clear();
        (await _db.AccessLogs.IgnoreQueryFilters().SingleAsync()).AccessGranted.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivatedMembership_EntersAgain()
    {
        var ms = await SeedActiveMembershipAsync();
        await FreezeAsync(ms.Id);
        await UnfreezeAsync(ms.Id);
        _db.ChangeTracker.Clear();

        var decision = await SwipeAsync();

        decision.Granted.Should().BeTrue("reactivada, vuelve a ser la membresía vigente");
    }

    [Fact]
    public async Task ReactivatedMembership_EntersAgain_EvenAfterBeingDeniedWhileFrozen()
    {
        var ms = await SeedActiveMembershipAsync();
        await FreezeAsync(ms.Id);
        _db.ChangeTracker.Clear();
        (await SwipeAsync()).Granted.Should().BeFalse("congelada, la puerta la deniega y deja un log");
        await UnfreezeAsync(ms.Id);
        _db.ChangeTracker.Clear();

        var decision = await SwipeAsync();

        decision.Granted.Should().BeTrue("un swipe denegado no es una entrada: no hay nadie 'adentro' que bloquear");
        decision.DenialReason.Should().BeNull();
    }
}
