namespace GymForge.Domain.Events;

public sealed record MemberCreatedEvent(Guid MemberId, Guid CompanyId, Guid SiteId);
public sealed record MemberActivatedEvent(Guid MemberId, DateOnly JoinDate);
public sealed record MemberCancelledEvent(Guid MemberId);
public sealed record MemberCheckedIn(Guid MemberId, bool HasDebtWarning, decimal OutstandingAmount);
