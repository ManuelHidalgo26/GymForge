using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Las lecturas de pantalla y de reportes no dejan entidades en el contexto de larga vida de Desktop (D9): si no,
/// la ficha seguiría mostrando el estado viejo aunque otro scope (el job diario) lo haya cambiado en la base.
/// </summary>
public class ReadsWithoutTrackingTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private GymForgeDbContext _db = null!;
    private Company _company = null!;
    private Site _site = null!;
    private Membership _membership = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out var tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.SaveChangesAsync();
        tenant.Set(_company.Id, _site.Id);

        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(Today);
        var plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        _membership = Membership.Create(_company.Id, _site.Id, member.Id, plan.Id, Today.AddDays(-20), Today.AddDays(3));
        _membership.Activate();
        await _db.Members.AddAsync(member);
        await _db.MembershipTypes.AddAsync(plan);
        await _db.Memberships.AddAsync(_membership);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task MembresiasDeUnSocio_NoQuedanTrackeadas_YLaSiguienteLecturaVeElEstadoActual()
    {
        var repo = new MembershipRepository(_db);
        (await repo.GetByMemberAsync(_membership.MemberId)).Should().ContainSingle()
            .Which.Status.Should().Be(MembershipStatus.Active);
        _db.ChangeTracker.Entries<Membership>().Should().BeEmpty();

        // Otro scope (el job diario) la deja vencida.
        await _db.Memberships.Where(m => m.Id == _membership.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MembershipStatus.Expired));

        (await repo.GetByMemberAsync(_membership.MemberId)).Single().Status.Should().Be(MembershipStatus.Expired);
    }

    [Fact]
    public async Task VencimientosYReportes_NoDejanEntidadesTrackeadas()
    {
        var repo = new MembershipRepository(_db);
        var from = DateTime.UtcNow.AddDays(-1);
        var to = DateTime.UtcNow.AddDays(1);

        await repo.GetExpiringAsync(_company.Id, Today, Today.AddDays(7));
        await repo.GetCancelledBetweenAsync(_company.Id, null, from, to);
        await repo.GetSoldBetweenAsync(_company.Id, null, from, to);
        await new PaymentRepository(_db).GetByPeriodAsync(_company.Id, _site.Id, from, to);
        await new PaymentRepository(_db).GetForBreakdownAsync(_company.Id, null, from, to);
        await new MemberRepository(_db).GetJoinedBetweenAsync(_company.Id, null, Today.AddDays(-1), Today.AddDays(1));
        await new SaleRepository(_db).GetCommissionLinesBetweenAsync(_company.Id, null, from, to);

        _db.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_SigueTrackeado_ParaPoderEscribir()
    {
        var loaded = await new MembershipRepository(_db).GetByIdAsync(_membership.Id);

        _db.ChangeTracker.Entries<Membership>().Should().ContainSingle(e => e.Entity == loaded);
    }
}
