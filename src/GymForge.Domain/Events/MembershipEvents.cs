namespace GymForge.Domain.Events;

public sealed record MembershipActivatedEvent(Guid MembershipId, Guid MemberId);
public sealed record MembershipFrozenEvent(Guid MembershipId, Guid MemberId, DateOnly FreezeStart, DateOnly FreezeEnd);
public sealed record MembershipCancelledEvent(Guid MembershipId, Guid MemberId, string Reason);
public sealed record MembershipExpiredEvent(Guid MembershipId, Guid MemberId);
