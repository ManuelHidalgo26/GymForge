using FluentAssertions;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>
/// Flujos completos (handler + repos + EF reales) que reproducen lo que hace la UI:
/// alta de socio y cobro. Cubren los bugs de "no se agrega el socio" y "sigue deudor".
/// </summary>
public class PaymentFlowIntegrationTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
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

        var company = Company.Create("Test Gym", "30-12345678-9");
        await _db.Companies.AddAsync(company);
        var site = Site.Create(company.Id, "Sede Central", "Av. Test 123");
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
    public async Task CreateMember_WithRealTenant_Persists()
    {
        var handler = new CreateMemberCommandHandler(new MemberRepository(_db));

        var dto = await handler.Handle(new CreateMemberCommand(
            _companyId, _site.Id, "Ana", "García",
            DocumentType.DNI, "30111222", Gender.Female,
            "ana@test.com", null, null), CancellationToken.None);

        (await _db.Members.CountAsync()).Should().Be(1);
        var loaded = await _db.Members.FindAsync(dto.Id);
        loaded.Should().NotBeNull();
        loaded!.CompanyId.Should().Be(_companyId);
        loaded.SiteId.Should().Be(_site.Id);
    }

    [Fact]
    public async Task ProcessPayment_SettlesCharge_AndPersistsPayment()
    {
        var member = Member.Create(_companyId, _site.Id, "Carlos", "López",
            DocumentType.DNI, "20111222", Gender.Male);
        await _db.Members.AddAsync(member);
        var charge = Charge.Create(_companyId, _site.Id, member.Id, null,
            ConceptType.MembershipFee, "Cuota mensual", 35_000m, DateOnly.FromDateTime(DateTime.Today));
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();

        var handler = new ProcessPaymentCommandHandler(
            new ChargeRepository(_db),
            new PaymentRepository(_db),
            new CashRegister(new ShiftRepository(_db)));

        await handler.Handle(new ProcessPaymentCommand(
            _companyId, _site.Id, member.Id, Guid.NewGuid(),
            35_000m, PaymentMethod.Cash, ShiftId: null,
            ChargeIds: new[] { charge.Id }), CancellationToken.None);

        // Releer desde la DB: el cargo quedó saldado y el pago persistido.
        _db.ChangeTracker.Clear();
        var reloaded = await _db.Charges.FindAsync(charge.Id);
        reloaded!.AmountOutstanding.Should().Be(0m);
        reloaded.Status.Should().Be(ChargeStatus.Paid);

        (await _db.Payments.CountAsync()).Should().Be(1);
        (await _db.PaymentAllocations.CountAsync()).Should().Be(1);
    }
}
