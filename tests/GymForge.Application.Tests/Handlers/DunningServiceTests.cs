using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class DunningServiceTests
{
    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly INotificationSender _sender = Substitute.For<INotificationSender>();

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 7, 21);

    public DunningServiceTests() =>
        _sender.SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>()).Returns(true);

    private DunningService NewService(bool enabled = true) =>
        new(_charges, _members, _sender, new DunningConfig { Enabled = enabled });

    private void HaveOverdue(params Charge[] charges) =>
        _charges.GetOverdueAsync(CompanyId, Today, Arg.Any<CancellationToken>())
            .Returns(charges.ToList());

    private static Charge OverdueCharge(int daysOverdue, decimal amount = 35_000m)
    {
        var due = Today.AddDays(-daysOverdue);
        return Charge.Create(CompanyId, SiteId, Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota mensual", amount, due);
    }

    private void MemberReturns(string? mobile)
    {
        var m = Member.Create(CompanyId, SiteId, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        m.UpdateContact("ana@test.com", mobile);
        _members.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(m);
    }

    [Fact]
    public async Task Sends_WhenChargeMatchesStageExactly()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 7));
        MemberReturns("+5491122334455");

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(1);
        await _sender.Received(1).SendAsync(
            Arg.Is<NotificationMessage>(m => m.ToPhone == "+5491122334455" && m.Body.Contains("PowerGym")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotSend_WhenDaysDoNotMatchAnyStage()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 5));   // 5 no es etapa (1,3,7,15,30)
        MemberReturns("+5491122334455");

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
        await _sender.DidNotReceive().SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DoesNotSend_WhenMemberHasNoMobile()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 7));
        MemberReturns(null);

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
        await _sender.DidNotReceive().SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SkipsFullyPaidCharges()
    {
        var paid = OverdueCharge(daysOverdue: 7);
        paid.ApplyPayment(paid.TotalAmount);   // saldo 0
        HaveOverdue(paid);
        MemberReturns("+5491122334455");

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
    }

    [Fact]
    public async Task DoesNothing_WhenDisabled()
    {
        var sent = await NewService(enabled: false).RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
        await _charges.DidNotReceive().GetOverdueAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}
