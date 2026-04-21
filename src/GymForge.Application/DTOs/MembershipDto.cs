using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record MembershipDto(
    Guid Id,
    Guid MemberId,
    Guid MembershipTypeId,
    string MembershipTypeName,
    DateOnly StartDate,
    DateOnly? EndDate,
    MembershipStatus Status,
    bool AutoRenew,
    int? VisitsRemaining,
    DateOnly? FreezeStart,
    DateOnly? FreezeEnd,
    DateTime? CancelDate,
    string? CancelReason)
{
    public static MembershipDto FromEntity(Membership ms) => new(
        ms.Id,
        ms.MemberId,
        ms.MembershipTypeId,
        ms.MembershipType?.Name ?? string.Empty,
        ms.StartDate,
        ms.EndDate,
        ms.Status,
        ms.AutoRenew,
        ms.VisitsRemaining,
        ms.FreezeStart,
        ms.FreezeEnd,
        ms.CancelDate,
        ms.CancelReason);
}
