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

/// <summary>
/// Salida de mora al cobrar o usar saldo a favor, con EF real: el socio vuelve a Active solo cuando saldó su
/// ÚLTIMO cargo vencido, y el cambio se persiste junto con el cobro. "Vencido" se mide contra el día local.
/// </summary>
public class ArrearsFlowIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly FakeClock _clock = FakeClock.OnDay(Today);
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

    private ProcessPaymentCommandHandler PaymentHandler() => new(
        new ChargeRepository(_db), new PaymentRepository(_db),
        new CashRegister(new ShiftRepository(_db)), new MemberCreditRepository(_db),
        TestServices.ArrearsRelief(_db, _clock));

    private ApplyMemberCreditCommandHandler ApplyCreditHandler() => new(
        new ChargeRepository(_db), new MemberCreditRepository(_db), TestServices.ArrearsRelief(_db, _clock));

    private async Task<Member> AddMemberAsync(Action<Member> transition)
    {
        var member = Member.Create(_company.Id, _site.Id, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        transition(member);
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private Task<Member> AddOverdueMemberAsync() => AddMemberAsync(m => m.MarkOverdue());

    private async Task<Charge> AddChargeDueAsync(Member member, DateOnly dueDate)
    {
        var charge = Charge.Create(_company.Id, _site.Id, member.Id, null,
            ConceptType.MembershipFee, "Cuota", 10_000m, dueDate);
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();
        return charge;
    }

    private Task<PaymentDto> PayAsync(Member member, decimal amount, params Guid[] chargeIds) =>
        PaymentHandler().Handle(
            new ProcessPaymentCommand(_company.Id, _site.Id, member.Id, Guid.NewGuid(),
                amount, PaymentMethod.Cash, ShiftId: null, ChargeIds: chargeIds.Length == 0 ? null : chargeIds),
            CancellationToken.None);

    private async Task<MemberStatus> PersistedStatusAsync(Member member)
    {
        _db.ChangeTracker.Clear();
        return (await _db.Members.SingleAsync(m => m.Id == member.Id)).Status;
    }

    // ── Cobro ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Payment_SettlingTheLastOverdueCharge_PersistsTheMemberAsActive()
    {
        var member = await AddOverdueMemberAsync();
        await AddChargeDueAsync(member, Today.AddDays(-10));

        await PayAsync(member, 10_000m);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task Payment_AutoAllocation_SettlesTheOldestAndKeepsTheMemberOverdueWhileAnotherRemains()
    {
        var member = await AddOverdueMemberAsync();
        await AddChargeDueAsync(member, Today.AddDays(-40));
        await AddChargeDueAsync(member, Today.AddDays(-10));

        await PayAsync(member, 10_000m);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Payment_OfAnExplicitCharge_KeepsTheMemberOverdueWhileAnotherIsStillDue()
    {
        var member = await AddOverdueMemberAsync();
        var chosen = await AddChargeDueAsync(member, Today.AddDays(-10));
        await AddChargeDueAsync(member, Today.AddDays(-40));

        await PayAsync(member, 10_000m, chosen.Id);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task Payment_PartialOfTheOnlyOverdueCharge_KeepsTheMemberOverdue()
    {
        var member = await AddOverdueMemberAsync();
        var charge = await AddChargeDueAsync(member, Today.AddDays(-10));

        await PayAsync(member, 4_000m);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Overdue);
        (await _db.Charges.SingleAsync(c => c.Id == charge.Id)).Status.Should().Be(ChargeStatus.PartiallyPaid);
    }

    [Fact]
    public async Task Payment_At2200Argentina_ACargoDueTodayLocalDoesNotKeepTheMemberOverdue()
    {
        // 22:00 del 21/07 en Argentina = 22/07 en UTC: con la fecha UTC la cuota que vence "hoy" ya contaría como mora.
        _clock.Now = FakeClock.At2200Argentina(Today).Now;
        var member = await AddOverdueMemberAsync();
        await AddChargeDueAsync(member, Today.AddDays(-10));
        await AddChargeDueAsync(member, Today);

        await PayAsync(member, 10_000m);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task Payment_ForASuspendedMember_DoesNotReactivateThem()
    {
        var member = await AddMemberAsync(m => m.Suspend());
        await AddChargeDueAsync(member, Today.AddDays(-10));

        await PayAsync(member, 10_000m);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Suspended);
    }

    // ── Saldo a favor ─────────────────────────────────────────────────────────

    /// <summary>Socio con saldo a favor que cae en mora después (si no, el propio cobro del excedente lo sacaría de mora).</summary>
    private async Task<Member> AddOverdueMemberWithCreditAsync(decimal credit)
    {
        var member = await AddMemberAsync(_ => { });
        await PayAsync(member, credit);   // sin cargos pendientes: todo queda como saldo a favor
        member.MarkOverdue();
        await _db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task ApplyCredit_SettlingTheLastOverdueCharge_PersistsTheMemberAsActive()
    {
        var member = await AddOverdueMemberWithCreditAsync(10_000m);
        await AddChargeDueAsync(member, Today.AddDays(-10));

        await ApplyCreditHandler().Handle(
            new ApplyMemberCreditCommand(_company.Id, _site.Id, member.Id), CancellationToken.None);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task ApplyCredit_NotCoveringEverythingOverdue_KeepsTheMemberOverdue()
    {
        var member = await AddOverdueMemberWithCreditAsync(10_000m);
        await AddChargeDueAsync(member, Today.AddDays(-40));
        await AddChargeDueAsync(member, Today.AddDays(-10));

        await ApplyCreditHandler().Handle(
            new ApplyMemberCreditCommand(_company.Id, _site.Id, member.Id), CancellationToken.None);

        (await PersistedStatusAsync(member)).Should().Be(MemberStatus.Overdue);
    }
}
