using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace GymForge.Integration.Tests;

/// <summary>Cuenta corriente del socio (ADR-003 punto 4): excedente → crédito → consumo, con EF real.</summary>
public class MemberCreditFlowIntegrationTests : IAsyncLifetime
{
    private readonly FakeClock _clock = FakeClock.OnDay(new DateOnly(2026, 7, 21));
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _site = null!;
    private Member _member = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        _member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.Members.AddAsync(_member);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private MemberCreditRepository CreditRepo() => new(_db);

    private ProcessPaymentCommandHandler PaymentHandler() => new(
        new ChargeRepository(_db), new PaymentRepository(_db),
        new CashRegister(new ShiftRepository(_db)), CreditRepo(), TestServices.ArrearsRelief(_db, _clock));

    private ApplyMemberCreditCommandHandler ApplyHandler() =>
        new(new ChargeRepository(_db), CreditRepo(), TestServices.ArrearsRelief(_db, _clock));

    private async Task<Charge> AddChargeAsync(decimal amount, DateOnly dueDate)
    {
        var charge = Charge.Create(_company.Id, _site.Id, _member.Id, null,
            ConceptType.MembershipFee, "Cuota mensual", amount, dueDate);
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();
        return charge;
    }

    private Task<PaymentDto> PayAsync(decimal amount, params Guid[] chargeIds) =>
        PaymentHandler().Handle(
            new ProcessPaymentCommand(_company.Id, _site.Id, _member.Id, Guid.NewGuid(),
                amount, PaymentMethod.Cash, ShiftId: null, ChargeIds: chargeIds.Length == 0 ? null : chargeIds),
            CancellationToken.None);

    private ApplyMemberCreditCommand ApplyCmd() => new(_company.Id, _site.Id, _member.Id);

    // ── Excedente → crédito ───────────────────────────────────────────────────

    [Fact]
    public async Task Overpayment_PersistsOneMovementAndTheBalance()
    {
        var charge = await AddChargeAsync(35_000m, new DateOnly(2026, 4, 1));

        var payment = await PayAsync(50_000m, charge.Id);

        _db.ChangeTracker.Clear();
        var movement = await _db.MemberCreditMovements.SingleAsync();
        movement.Amount.Should().Be(15_000m);
        movement.Type.Should().Be(CreditMovementType.Overpayment);
        movement.PaymentId.Should().Be(payment.Id);
        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(15_000m);
        (await _db.Charges.SingleAsync()).Status.Should().Be(ChargeStatus.Paid);
    }

    [Fact]
    public async Task ExactPayment_CreatesNoMovement()
    {
        var charge = await AddChargeAsync(35_000m, new DateOnly(2026, 4, 1));

        await PayAsync(35_000m, charge.Id);

        (await _db.MemberCreditMovements.CountAsync()).Should().Be(0);
        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(0m);
    }

    [Fact]
    public async Task Balance_KeepsCentsExact()
    {
        await PayAsync(100.20m);
        for (var i = 0; i < 10; i++)
            await PayAsync(0.10m);   // la suma en punto flotante se desviaría

        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(101.20m);
    }

    // ── Consumo ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyCredit_SettlesTheChargeAndEmptiesTheBalance()
    {
        await PayAsync(15_000m);   // sin cargos pendientes: todo queda como saldo a favor
        var charge = await AddChargeAsync(15_000m, new DateOnly(2026, 5, 1));

        var result = await ApplyHandler().Handle(ApplyCmd(), CancellationToken.None);

        result.Should().Be(new ApplyCreditResult(15_000m, 0m));
        _db.ChangeTracker.Clear();
        (await _db.Charges.SingleAsync(c => c.Id == charge.Id)).Status.Should().Be(ChargeStatus.Paid);
        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(0m);

        var ledger = await CreditRepo().GetByMemberAsync(_member.Id);
        ledger.Select(m => m.Type).Should().BeEquivalentTo(
            new[] { CreditMovementType.Overpayment, CreditMovementType.AppliedToCharge });
        ledger.Single(m => m.Type == CreditMovementType.AppliedToCharge).ChargeId.Should().Be(charge.Id);
    }

    [Fact]
    public async Task ApplyCredit_PartiallyCoversTheOldestChargeFirst()
    {
        await PayAsync(12_000m);
        var older = await AddChargeAsync(10_000m, new DateOnly(2026, 3, 1));
        var newer = await AddChargeAsync(10_000m, new DateOnly(2026, 4, 1));

        await ApplyHandler().Handle(ApplyCmd(), CancellationToken.None);

        _db.ChangeTracker.Clear();
        (await _db.Charges.SingleAsync(c => c.Id == older.Id)).Status.Should().Be(ChargeStatus.Paid);
        var partial = await _db.Charges.SingleAsync(c => c.Id == newer.Id);
        partial.Status.Should().Be(ChargeStatus.PartiallyPaid);
        partial.AmountOutstanding.Should().Be(8_000m);
    }

    [Fact]
    public async Task ApplyCredit_WithoutBalance_ChangesNothing()
    {
        var charge = await AddChargeAsync(10_000m, new DateOnly(2026, 3, 1));

        var result = await ApplyHandler().Handle(ApplyCmd(), CancellationToken.None);

        result.Applied.Should().Be(0m);
        _db.ChangeTracker.Clear();
        (await _db.Charges.SingleAsync(c => c.Id == charge.Id)).AmountPaid.Should().Be(0m);
        (await _db.MemberCreditMovements.CountAsync()).Should().Be(0);
    }

    // ── Aislamiento ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Balance_IsIsolatedByCompanyAndSite()
    {
        await PayAsync(9_000m);
        var otherCompany = Company.Create("Otro Gym", "30-98765432-1");
        var otherSite = Site.Create(otherCompany.Id, "Otra", "Calle 1");
        var otherSameCompanySite = Site.Create(_company.Id, "Sucursal", "Calle 2");
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddRangeAsync(otherSite, otherSameCompanySite);
        await _db.SaveChangesAsync();

        decimal fromOtherCompany;
        decimal fromOtherSite;
        using (_tenant.BeginScope(otherCompany.Id, otherSite.Id))
            fromOtherCompany = await CreditRepo().GetBalanceAsync(_member.Id);
        using (_tenant.BeginScope(_company.Id, otherSameCompanySite.Id))
            fromOtherSite = await CreditRepo().GetBalanceAsync(_member.Id);

        fromOtherCompany.Should().Be(0m);
        fromOtherSite.Should().Be(0m);
        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(9_000m);
    }

    [Fact]
    public async Task ApplyCredit_ForAMemberOfAnotherTenant_IsANoOp()
    {
        await PayAsync(9_000m);
        var foreignMember = Guid.NewGuid();

        var result = await ApplyHandler().Handle(
            new ApplyMemberCreditCommand(_company.Id, _site.Id, foreignMember), CancellationToken.None);

        result.Should().Be(new ApplyCreditResult(0m, 0m));
        (await CreditRepo().GetBalanceAsync(_member.Id)).Should().Be(9_000m);
    }
}
