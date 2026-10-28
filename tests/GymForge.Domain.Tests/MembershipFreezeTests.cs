using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

/// <summary>Tope anual de congelamiento y descongelamiento anticipado (ADR-003 punto 3).</summary>
public class MembershipFreezeTests
{
    private const int YearlyCap = 90;

    private static readonly DateOnly Start2026 = new(2026, 1, 1);

    private static Membership ActiveMembership(DateOnly? endDate = null)
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Start2026, endDate ?? new DateOnly(2028, 1, 1));
        ms.Activate();
        return ms;
    }

    /// <summary>Congela <paramref name="days"/> días desde <paramref name="from"/> y descongela pasado el fin.</summary>
    private static void FreezeAndFinish(Membership ms, DateOnly from, int days)
    {
        var end = from.AddDays(days);
        ms.Freeze(from, end, "Test", YearlyCap);
        ms.Unfreeze(end.AddDays(1));
    }

    // ── Tope anual ────────────────────────────────────────────────────────────

    [Fact]
    public void Freeze_ExceedsYearlyCap_Throws()
    {
        var ms = ActiveMembership();
        FreezeAndFinish(ms, new DateOnly(2026, 2, 1), 80);

        var act = () => ms.Freeze(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), "Viaje", YearlyCap);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*90*usados 80*solicitados 20*");
    }

    [Fact]
    public void Freeze_ExceedsYearlyCap_LeavesMembershipUntouched()
    {
        var ms = ActiveMembership();
        FreezeAndFinish(ms, new DateOnly(2026, 2, 1), 80);
        var endBefore = ms.EndDate;

        var act = () => ms.Freeze(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 21), "Viaje", YearlyCap);

        act.Should().Throw<InvalidOperationException>();
        ms.Status.Should().Be(MembershipStatus.Active);
        ms.EndDate.Should().Be(endBefore);
        ms.FreezeDaysUsedYear.Should().Be(80);
        ms.FreezeStart.Should().BeNull();
    }

    [Fact]
    public void Freeze_AccumulatesWithinYear()
    {
        var ms = ActiveMembership();

        FreezeAndFinish(ms, new DateOnly(2026, 2, 1), 40);
        FreezeAndFinish(ms, new DateOnly(2026, 5, 1), 40);

        ms.FreezeDaysUsedYear.Should().Be(80);
        ms.FreezeYear.Should().Be(2026);
        var third = () => ms.Freeze(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 21), "Otra vez", YearlyCap);
        third.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Freeze_ExactlyAtCap_Succeeds()
    {
        var ms = ActiveMembership();

        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1).AddDays(90), "Largo", YearlyCap);

        ms.Status.Should().Be(MembershipStatus.Frozen);
        ms.FreezeDaysUsedYear.Should().Be(90);
    }

    [Fact]
    public void Freeze_ResetsCounterOnNewYear()
    {
        var ms = ActiveMembership();
        FreezeAndFinish(ms, new DateOnly(2026, 11, 1), 80);

        ms.Freeze(new DateOnly(2027, 1, 10), new DateOnly(2027, 2, 9), "Año nuevo", YearlyCap);

        ms.FreezeYear.Should().Be(2027);
        ms.FreezeDaysUsedYear.Should().Be(30);
        ms.FreezeCountUsed.Should().Be(110, "el acumulado histórico no se reinicia");
    }

    [Fact]
    public void Freeze_UsesTheCapPassedByTheCompany()
    {
        var ms = ActiveMembership();

        var act = () => ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), "Corto", maxFreezeDaysPerYear: 30);

        act.Should().Throw<InvalidOperationException>().WithMessage("*30*");
    }

    [Fact]
    public void Freeze_ExtendsEndDateByTheRequestedDays()
    {
        var ms = ActiveMembership(endDate: new DateOnly(2026, 12, 31));

        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje", YearlyCap);

        ms.EndDate.Should().Be(new DateOnly(2027, 1, 30));
    }

    [Fact]
    public void FreezeDaysUsedIn_NeverFrozen_IsZero()
    {
        ActiveMembership().FreezeDaysUsedIn(2026).Should().Be(0);
    }

    [Fact]
    public void FreezeDaysUsedIn_OnlyCountsTheYearOfTheQuotaInCourse()
    {
        var ms = ActiveMembership();
        FreezeAndFinish(ms, new DateOnly(2026, 2, 1), 40);

        ms.FreezeDaysUsedIn(2026).Should().Be(40);
        ms.FreezeDaysUsedIn(2027).Should().Be(0, "el cupo se reinicia con el año calendario");
        ms.FreezeDaysUsedIn(2025).Should().Be(0);
    }

    // ── Descongelamiento ──────────────────────────────────────────────────────

    [Fact]
    public void Unfreeze_EarlyReturnsEndDateAndQuota()
    {
        var ms = ActiveMembership(endDate: new DateOnly(2026, 12, 31));
        var freezeStart = new DateOnly(2026, 3, 1);
        ms.Freeze(freezeStart, freezeStart.AddDays(30), "Viaje", YearlyCap);
        ms.EndDate.Should().Be(new DateOnly(2027, 1, 30));

        ms.Unfreeze(freezeStart.AddDays(10));

        ms.Status.Should().Be(MembershipStatus.Active);
        ms.EndDate.Should().Be(new DateOnly(2027, 1, 10), "se deshacen los 20 días de extensión no consumidos");
        ms.FreezeDaysUsedYear.Should().Be(10);
        ms.FreezeCountUsed.Should().Be(10);
    }

    [Fact]
    public void Unfreeze_AfterFreezeEnd_NoAdjustment()
    {
        var ms = ActiveMembership(endDate: new DateOnly(2026, 12, 31));
        var freezeStart = new DateOnly(2026, 3, 1);
        var freezeEnd = freezeStart.AddDays(30);
        ms.Freeze(freezeStart, freezeEnd, "Viaje", YearlyCap);

        ms.Unfreeze(freezeEnd.AddDays(5));

        ms.EndDate.Should().Be(new DateOnly(2027, 1, 30));
        ms.FreezeDaysUsedYear.Should().Be(30);
        ms.FreezeCountUsed.Should().Be(30);
    }

    [Fact]
    public void Unfreeze_BeforeFreezeStarts_ReturnsAllRequestedDays()
    {
        var ms = ActiveMembership(endDate: new DateOnly(2026, 12, 31));
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Se arrepintió", YearlyCap);

        ms.Unfreeze(new DateOnly(2026, 2, 20));

        ms.EndDate.Should().Be(new DateOnly(2026, 12, 31), "nunca se consumió ningún día de freeze");
        ms.FreezeDaysUsedYear.Should().Be(0);
        ms.FreezeCountUsed.Should().Be(0);
    }

    [Fact]
    public void Unfreeze_ClearsFreezeDetails()
    {
        var ms = ActiveMembership();
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje", YearlyCap);

        ms.Unfreeze(new DateOnly(2026, 3, 15));

        ms.FreezeStart.Should().BeNull();
        ms.FreezeEnd.Should().BeNull();
        ms.FreezeReason.Should().BeNull();
    }

    [Fact]
    public void Unfreeze_EarlyReleaseFreesQuotaForANewFreeze()
    {
        var ms = ActiveMembership();
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1).AddDays(90), "Largo", YearlyCap);
        ms.Unfreeze(new DateOnly(2026, 3, 11));

        var act = () => ms.Freeze(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1).AddDays(80), "Otra", YearlyCap);

        act.Should().NotThrow("el descongelamiento anticipado devolvió 80 días de cupo");
    }

    [Fact]
    public void Unfreeze_WhenNotFrozen_Throws()
    {
        var ms = ActiveMembership();

        var act = () => ms.Unfreeze(new DateOnly(2026, 3, 1));

        act.Should().Throw<InvalidOperationException>();
    }
}
