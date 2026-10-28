using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class AccessLogTests
{
    [Fact]
    public void DeniedUnknown_RecordsTheSwipeWithoutAMember()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();

        var log = AccessLog.DeniedUnknown(
            companyId, siteId, doorId: 2, AccessMethod.RfidCard, AccessDenialReason.TagUnknown, "TAG-404");

        log.MemberId.Should().BeNull();
        log.MembershipId.Should().BeNull();
        log.CompanyId.Should().Be(companyId);
        log.SiteId.Should().Be(siteId);
        log.DoorId.Should().Be(2);
        log.Method.Should().Be(AccessMethod.RfidCard);
        log.Direction.Should().Be(AccessDirection.In);
        log.TagSerial.Should().Be("TAG-404");
        log.AccessGranted.Should().BeFalse();
        log.DenialReason.Should().Be(AccessDenialReason.TagUnknown);
    }

    [Fact]
    public void Denied_KeepsTheMember()
    {
        var memberId = Guid.NewGuid();

        var log = AccessLog.Denied(
            Guid.NewGuid(), Guid.NewGuid(), memberId, 1, AccessMethod.RfidCard, AccessDenialReason.Owing);

        log.MemberId.Should().Be(memberId);
        log.AccessGranted.Should().BeFalse();
    }
}
