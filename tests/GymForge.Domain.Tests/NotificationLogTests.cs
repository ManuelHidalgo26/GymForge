using FluentAssertions;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Domain.Tests;

public class NotificationLogTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly Guid MemberId = Guid.NewGuid();

    [Fact]
    public void Manual_RecordsTheOperatorReminder_WithoutRequiringACharge()
    {
        var before = DateTime.UtcNow;

        var log = NotificationLog.Manual(
            CompanyId, SiteId, MemberId, chargeId: null, NotificationChannel.WhatsApp,
            stageDaysOverdue: null, toPhone: "+5491122334455");

        log.CompanyId.Should().Be(CompanyId);
        log.SiteId.Should().Be(SiteId);
        log.MemberId.Should().Be(MemberId);
        log.ChargeId.Should().BeNull();
        log.Channel.Should().Be(NotificationChannel.WhatsApp);
        log.Trigger.Should().Be(NotificationTrigger.Manual);
        log.StageDaysOverdue.Should().BeNull();
        log.ToPhone.Should().Be("+5491122334455");
        log.SentAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Automatic_RecordsTheChargeAndTheStageThatFired()
    {
        var chargeId = Guid.NewGuid();

        var log = NotificationLog.Automatic(
            CompanyId, SiteId, MemberId, chargeId, NotificationChannel.WhatsApp,
            stageDaysOverdue: 7, toPhone: "+5491122334455");

        log.Trigger.Should().Be(NotificationTrigger.Automatic);
        log.ChargeId.Should().Be(chargeId);
        log.StageDaysOverdue.Should().Be(7);
        log.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void IsSiteScoped_ButNotAuditable()
    {
        var log = NotificationLog.Manual(
            CompanyId, SiteId, MemberId, null, NotificationChannel.WhatsApp, null, null);

        log.Should().BeAssignableTo<ISiteScoped>();
        log.Should().NotBeAssignableTo<IAuditable>("el log es su propia auditoría");
    }
}
