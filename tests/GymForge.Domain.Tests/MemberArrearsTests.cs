using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.Events;

namespace GymForge.Domain.Tests;

/// <summary>Salida de mora del socio: solo Overdue vuelve a Active, sin ruido de alta.</summary>
public class MemberArrearsTests
{
    private static Member ActiveMember()
    {
        var member = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        member.ClearDomainEvents();
        return member;
    }

    private static Member OverdueMember()
    {
        var member = ActiveMember();
        member.MarkOverdue();
        return member;
    }

    /// <summary>Hay estados (Expired) que el dominio todavía no alcanza con un método: se fuerzan.</summary>
    private static Member MemberIn(MemberStatus status)
    {
        var member = ActiveMember();
        typeof(Member).GetProperty(nameof(Member.Status))!.SetValue(member, status);
        return member;
    }

    [Fact]
    public void ClearArrears_OverdueMember_BecomesActive()
    {
        var member = OverdueMember();

        member.ClearArrears();

        member.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public void ClearArrears_OverdueMember_DoesNotRaiseAnActivationEvent()
    {
        var member = OverdueMember();

        member.ClearArrears();

        member.DomainEvents.Should().NotContain(e => e is MemberActivatedEvent);
    }

    [Fact]
    public void ClearArrears_OverdueMember_KeepsTheOriginalJoinDate()
    {
        var member = OverdueMember();

        member.ClearArrears();

        member.JoinDate.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void ClearArrears_IsIdempotent()
    {
        var member = OverdueMember();

        member.ClearArrears();
        var updatedAt = member.UpdatedAt;
        member.ClearArrears();

        member.Status.Should().Be(MemberStatus.Active);
        member.UpdatedAt.Should().Be(updatedAt, "la segunda llamada no cambia nada");
    }

    [Fact]
    public void ClearArrears_ActiveMember_IsANoOp()
    {
        var member = ActiveMember();
        var updatedAt = member.UpdatedAt;

        member.ClearArrears();

        member.Status.Should().Be(MemberStatus.Active);
        member.UpdatedAt.Should().Be(updatedAt);
    }

    [Theory]
    [InlineData(MemberStatus.Prospect)]
    [InlineData(MemberStatus.Frozen)]
    [InlineData(MemberStatus.Suspended)]
    [InlineData(MemberStatus.Expired)]
    [InlineData(MemberStatus.Cancelled)]
    public void ClearArrears_AnyOtherStatus_IsLeftUntouched(MemberStatus status)
    {
        var member = MemberIn(status);
        var updatedAt = member.UpdatedAt;

        member.ClearArrears();

        member.Status.Should().Be(status);
        member.UpdatedAt.Should().Be(updatedAt);
    }
}
