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

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out var tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.SaveChangesAsync();
        tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private async Task<Charge> AddChargeAsync(Action<Charge>? configure = null)
    {
        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI,
            Guid.NewGuid().ToString("N")[..8], Gender.Female);
        var charge = Charge.Create(_company.Id, _site.Id, member.Id, null,
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
}
