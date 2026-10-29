using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// La lectura de mora de la base y la regla de dominio (<see cref="Charge.IsInArrears"/>) tienen que coincidir
/// en qué cargos cuentan: un reembolsado no es mora, ni para el job, ni para el dunning, ni para las pantallas.
/// </summary>
public class ChargeOverdueQueryIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly AsOf = new(2026, 7, 21);

    private GymForgeDbContext _db = null!;
    private Company _company = null!;
    private Site _site = null!;
    private Site _otherSite = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out var tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        _otherSite = Site.Create(_company.Id, "Norte", "Calle Norte 456");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddRangeAsync(_site, _otherSite);
        await _db.SaveChangesAsync();
        tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private async Task<Charge> AddChargeAsync(Action<Charge>? configure = null, Site? site = null)
    {
        site ??= _site;
        var member = Member.Create(_company.Id, site.Id, "Ana", "García", DocumentType.DNI,
            Guid.NewGuid().ToString("N")[..8], Gender.Female);
        var charge = Charge.Create(_company.Id, site.Id, member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, AsOf.AddDays(-5));
        configure?.Invoke(charge);
        await _db.Members.AddAsync(member);
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();
        return charge;
    }

    [Theory]
    [InlineData(ChargeStatus.Paid)]
    [InlineData(ChargeStatus.WrittenOff)]
    [InlineData(ChargeStatus.Refunded)]
    public async Task GetOverdue_ExcludesChargesThatNoLongerCount(ChargeStatus status)
    {
        await AddChargeAsync(c =>
            typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(c, status));

        var overdue = await new ChargeRepository(_db).GetOverdueAsync(_company.Id, AsOf);

        overdue.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdue_IncludesAPendingChargeDueBeforeTheDate()
    {
        var charge = await AddChargeAsync();

        var overdue = await new ChargeRepository(_db).GetOverdueAsync(_company.Id, AsOf);

        overdue.Should().ContainSingle().Which.Id.Should().Be(charge.Id);
        charge.IsInArrears(AsOf).Should().BeTrue();
    }

    // ── Mora de una sola sede (pantalla de morosos) ───────────────────────────

    [Fact]
    public async Task GetOverdueBySite_ReturnsOnlyTheMoraOfThatSite()
    {
        var central = await AddChargeAsync();
        var norte = await AddChargeAsync(site: _otherSite);

        var repo = new ChargeRepository(_db);

        (await repo.GetOverdueBySiteAsync(_company.Id, _site.Id, AsOf)).Select(c => c.Id)
            .Should().Equal(central.Id);
        (await repo.GetOverdueBySiteAsync(_company.Id, _otherSite.Id, AsOf)).Select(c => c.Id)
            .Should().Equal(norte.Id);
        (await repo.GetOverdueAsync(_company.Id, AsOf)).Select(c => c.Id)
            .Should().BeEquivalentTo([central.Id, norte.Id], "el job sigue viendo todas las sedes");
    }

    [Fact]
    public async Task GetOverdueBySite_NeverCrossesCompanies()
    {
        await AddChargeAsync();

        (await new ChargeRepository(_db).GetOverdueBySiteAsync(Guid.NewGuid(), _site.Id, AsOf)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueBySite_DoesNotDependOnTheAmbientTenant()
    {
        var norte = await AddChargeAsync(site: _otherSite);   // el tenant ambiente es la sede Central

        var overdue = await new ChargeRepository(_db).GetOverdueBySiteAsync(_company.Id, _otherSite.Id, AsOf);

        overdue.Should().ContainSingle().Which.Id.Should().Be(norte.Id);
    }

    [Fact]
    public async Task GetOverdueBySite_IncludesTheMember_AndOrdersByDueDate()
    {
        var recent = await AddChargeAsync();
        var oldest = await AddChargeAsync(c =>
            typeof(Charge).GetProperty(nameof(Charge.DueDate))!.SetValue(c, AsOf.AddDays(-30)));

        var overdue = await new ChargeRepository(_db).GetOverdueBySiteAsync(_company.Id, _site.Id, AsOf);

        overdue.Select(c => c.Id).Should().Equal(oldest.Id, recent.Id);
        overdue.Should().OnlyContain(c => c.Member != null && c.Member.FirstName == "Ana");
    }

    [Theory]
    [InlineData(ChargeStatus.Paid)]
    [InlineData(ChargeStatus.WrittenOff)]
    [InlineData(ChargeStatus.Refunded)]
    public async Task GetOverdueBySite_ExcludesChargesThatNoLongerCount(ChargeStatus status)
    {
        await AddChargeAsync(c => typeof(Charge).GetProperty(nameof(Charge.Status))!.SetValue(c, status));

        (await new ChargeRepository(_db).GetOverdueBySiteAsync(_company.Id, _site.Id, AsOf)).Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverdueBySite_ExcludesWhatIsNotYetDue()
    {
        await AddChargeAsync(c =>
            typeof(Charge).GetProperty(nameof(Charge.DueDate))!.SetValue(c, AsOf));   // vence hoy: todavía no es mora

        (await new ChargeRepository(_db).GetOverdueBySiteAsync(_company.Id, _site.Id, AsOf)).Should().BeEmpty();
    }
}
