using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class MembershipStateMachineTests
{
    private static Membership CreateActiveMembership() =>
        CreateMembershipInStatus(MembershipStatus.PendingActivation);

    private static Membership CreateMembershipInStatus(MembershipStatus status)
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddMonths(1)));

        if (status == MembershipStatus.Active)
            ms.Activate();

        return ms;
    }

    // ── Activation ────────────────────────────────────────────────────────────

    [Fact]
    public void PendingActivation_Activate_BecomesActive()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.PendingActivation);
        ms.Activate();
        ms.Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public void Active_Activate_Throws()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.Active);
        var act = () => ms.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Freeze ────────────────────────────────────────────────────────────────

    [Fact]
    public void Active_Freeze_BecomesFrozen_AndExtendsEndDate()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.Active);
        var originalEnd = ms.EndDate!.Value;
        var freezeStart = DateOnly.FromDateTime(DateTime.Today.AddDays(2));
        var freezeEnd   = DateOnly.FromDateTime(DateTime.Today.AddDays(12));

        ms.Freeze(freezeStart, freezeEnd, "Viaje");

        ms.Status.Should().Be(MembershipStatus.Frozen);
        ms.FreezeStart.Should().Be(freezeStart);
        ms.FreezeEnd.Should().Be(freezeEnd);
        ms.EndDate.Should().Be(originalEnd.AddDays(10));
        ms.FreezeCountUsed.Should().Be(10);
    }

    [Fact]
    public void Frozen_Unfreeze_BecomesActive()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.Active);
        ms.Freeze(
            DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            DateOnly.FromDateTime(DateTime.Today.AddDays(11)),
            "Test");

        ms.Unfreeze();
        ms.Status.Should().Be(MembershipStatus.Active);
        ms.FreezeStart.Should().BeNull();
    }

    [Fact]
    public void Inactive_Freeze_Throws()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.PendingActivation);
        var act = () => ms.Freeze(
            DateOnly.FromDateTime(DateTime.Today),
            DateOnly.FromDateTime(DateTime.Today.AddDays(10)),
            "Test");
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public void Active_Cancel_SetsCancelledWithReason()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.Active);
        ms.Cancel("Cliente se mudó");

        ms.Status.Should().Be(MembershipStatus.Cancelled);
        ms.CancelReason.Should().Be("Cliente se mudó");
        ms.CancelDate.Should().NotBeNull();
    }

    // ── Visit pack ────────────────────────────────────────────────────────────

    [Fact]
    public void VisitPack_DecrementVisit_DecrementsCounter()
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today), null, visitsRemaining: 10);
        ms.Activate();

        ms.DecrementVisit();
        ms.VisitsRemaining.Should().Be(9);
    }

    [Fact]
    public void VisitPack_Exhausted_DecrementThrows()
    {
        var ms = Membership.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            DateOnly.FromDateTime(DateTime.Today), null, visitsRemaining: 0);
        ms.Activate();

        var act = () => ms.DecrementVisit();
        act.Should().Throw<InvalidOperationException>("No quedan visitas");
    }

    [Fact]
    public void OpenEnded_DecrementVisit_IsNoOp()
    {
        var ms = CreateMembershipInStatus(MembershipStatus.Active);
        ms.VisitsRemaining.Should().BeNull();
        var act = () => ms.DecrementVisit();
        act.Should().NotThrow();
    }
}
