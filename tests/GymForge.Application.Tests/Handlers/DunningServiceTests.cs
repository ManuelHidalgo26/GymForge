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
    private readonly INotificationLogRepository _log = Substitute.For<INotificationLogRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IDisposable _tenantScope = Substitute.For<IDisposable>();

    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 7, 21);

    public DunningServiceTests()
    {
        _sender.SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>()).Returns(true);
        _tenant.BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(_tenantScope);
    }

    private DunningService NewService(bool enabled = true, IReadOnlyList<DunningStage>? stages = null)
    {
        var config = new DunningConfig { Enabled = enabled };
        if (stages is not null) config.Stages = stages;
        return new(_charges, _members, _sender, _log, _tenant, config);
    }

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
            Arg.Is<NotificationMessage>(m =>
                m.ToPhone == "+5491122334455" && m.Body.Contains("PowerGym") && m.Body.Contains("$35.000")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RendersTheEditableTemplateOfTheMatchingStage()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 7));
        MemberReturns("+5491122334455");
        var stages = new[] { new DunningStage(7, "Propia", "Che {nombre}, {dias} días de atraso en {gimnasio}") };

        await NewService(stages: stages).RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        await _sender.Received(1).SendAsync(
            Arg.Is<NotificationMessage>(m => m.Body == "Che Ana, 7 días de atraso en PowerGym"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LooksUpEachMemberInsideTheSiteOfTheirCharge()
    {
        // El job recorre la mora de TODAS las sedes; el socio vive en la sede de su cargo.
        var otherSiteId = Guid.NewGuid();
        var inSite = OverdueCharge(daysOverdue: 7);
        var inOtherSite = Charge.Create(CompanyId, otherSiteId, Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota mensual", 35_000m, Today.AddDays(-7));
        HaveOverdue(inSite, inOtherSite);
        MemberReturns("+5491122334455");

        await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        _tenant.Received(1).BeginScope(CompanyId, SiteId);
        _tenant.Received(1).BeginScope(CompanyId, otherSiteId);
        _tenantScope.Received(2).Dispose();
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

    // ── NotificationLog: traza y dedup por (cargo, etapa) ─────────────────────

    [Fact]
    public async Task Envia_Y_RegistraNotificationLog()
    {
        var charge = OverdueCharge(daysOverdue: 7);
        HaveOverdue(charge);
        MemberReturns("+5491122334455");

        await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n =>
                n.Trigger == NotificationTrigger.Automatic &&
                n.Channel == NotificationChannel.WhatsApp &&
                n.CompanyId == CompanyId && n.SiteId == SiteId &&
                n.MemberId == charge.MemberId && n.ChargeId == charge.Id &&
                n.StageDaysOverdue == 7 && n.ToPhone == "+5491122334455"),
            Arg.Any<CancellationToken>());
        await _log.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoReenvia_SiYaSeRegistroEsaEtapaParaEseCargo()
    {
        var charge = OverdueCharge(daysOverdue: 7);
        HaveOverdue(charge);
        MemberReturns("+5491122334455");
        _log.WasSentAsync(CompanyId, charge.Id, 7, Arg.Any<CancellationToken>()).Returns(true);

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
        await _sender.DidNotReceive().SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>());
        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DistintaEtapaDelMismoCargo_SeEnviaYRegistra()
    {
        var charge = OverdueCharge(daysOverdue: 7);
        HaveOverdue(charge);
        MemberReturns("+5491122334455");
        _log.WasSentAsync(CompanyId, charge.Id, 3, Arg.Any<CancellationToken>()).Returns(true);   // la de 3 ya salió

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(1);
        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n => n.StageDaysOverdue == 7), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SiElSenderRechazaElMensaje_NoRegistraNiCuenta()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 7));
        MemberReturns("+5491122334455");
        _sender.SendAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>()).Returns(false);

        var sent = await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        sent.Should().Be(0);
        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinCelular_NoRegistraNada()
    {
        HaveOverdue(OverdueCharge(daysOverdue: 7));
        MemberReturns(null);

        await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegistraElAvisoEnLaSedeDeSuCargo()
    {
        var otherSiteId = Guid.NewGuid();
        var inOtherSite = Charge.Create(CompanyId, otherSiteId, Guid.NewGuid(), null,
            ConceptType.MembershipFee, "Cuota mensual", 35_000m, Today.AddDays(-7));
        HaveOverdue(inOtherSite);
        MemberReturns("+5491122334455");

        await NewService().RunAsync(CompanyId, Today, "PowerGym", CancellationToken.None);

        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n => n.SiteId == otherSiteId), Arg.Any<CancellationToken>());
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
