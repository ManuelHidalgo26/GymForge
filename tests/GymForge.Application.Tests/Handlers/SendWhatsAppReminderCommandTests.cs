using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class SendWhatsAppReminderCommandTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly INotificationLogRepository _log = Substitute.For<INotificationLogRepository>();
    private readonly DunningConfig _config = new();

    private readonly Company _company = Company.Create("PowerGym", "30-71234567-3");
    private readonly Guid _siteId = Guid.NewGuid();
    private Member _member = null!;

    public SendWhatsAppReminderCommandTests()
    {
        _sites.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
        _calendar.TodayFor(_company).Returns(Today);
        HaveMember("11 2345-6789");
    }

    private SendWhatsAppReminderCommandHandler Handler() =>
        new(_members, _charges, _sites, _calendar, _log, _config);

    private SendWhatsAppReminderCommand Command() => new(_company.Id, _siteId, _member.Id);

    private void HaveMember(string? mobile)
    {
        _member = Member.Create(_company.Id, _siteId, "Ana", "García", DocumentType.DNI, "30111222", Gender.Female);
        _member.UpdateContact("ana@test.com", mobile);
        _members.GetByIdAsync(_member.Id, Arg.Any<CancellationToken>()).Returns(_member);
    }

    private Charge ChargeDue(int daysOverdue, decimal amount = 35_000m, Guid? memberId = null) =>
        Charge.Create(_company.Id, _siteId, memberId ?? _member.Id, null,
            ConceptType.MembershipFee, "Cuota mensual", amount, Today.AddDays(-daysOverdue));

    private void HaveOverdue(params Charge[] charges) =>
        _charges.GetOverdueBySiteAsync(_company.Id, _siteId, Today, Arg.Any<CancellationToken>())
            .Returns(charges.ToList());

    [Fact]
    public async Task ArmaElLink_ConElMensajeDeLaEtapaVigente_YDevuelveLaUrl()
    {
        HaveOverdue(ChargeDue(daysOverdue: 20));   // vigente: la de 15 días ("Urgente")

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.PhoneUsable.Should().BeTrue();
        result.Reason.Should().BeNull();
        var expectedMessage = DunningMessageRenderer.Render(
            DunningConfig.DefaultStages[3].Template,
            new DunningContext("Ana", "PowerGym", 35_000m, Today.AddDays(-20), 20));
        result.WhatsAppUrl.Should().Be(WhatsAppLink.Build("11 2345-6789", expectedMessage));
        result.WhatsAppUrl.Should().StartWith("https://wa.me/5491123456789?text=");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 3)]
    [InlineData(7, 7)]
    [InlineData(29, 15)]
    [InlineData(45, 30)]
    public async Task RegistraElAvisoManual_ConLaEtapaCorrectaPorAntiguedad(int daysOverdue, int expectedStage)
    {
        var charge = ChargeDue(daysOverdue);
        HaveOverdue(charge);

        await Handler().Handle(Command(), CancellationToken.None);

        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n =>
                n.Trigger == NotificationTrigger.Manual &&
                n.Channel == NotificationChannel.WhatsApp &&
                n.CompanyId == _company.Id && n.SiteId == _siteId && n.MemberId == _member.Id &&
                n.ChargeId == charge.Id && n.StageDaysOverdue == expectedStage &&
                n.ToPhone == "11 2345-6789"),
            Arg.Any<CancellationToken>());
        await _log.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConVariosCargos_SumaElSaldo_YTomaLaAntiguedadDelMasViejo()
    {
        var oldest = ChargeDue(daysOverdue: 16, amount: 30_000m);
        var newest = ChargeDue(daysOverdue: 2, amount: 5_000m);
        HaveOverdue(newest, oldest);
        _config.Stages = [new DunningStage(1, "Unica", "{monto} / {dias} / {vencimiento}")];

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.WhatsAppUrl.Should().EndWith(Uri.EscapeDataString($"$35.000 / 16 / {Today.AddDays(-16):dd/MM/yyyy}"));
        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n => n.ChargeId == oldest.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IgnoraLosCargosDeOtrosSocios()
    {
        HaveOverdue(ChargeDue(daysOverdue: 20, amount: 10_000m), ChargeDue(daysOverdue: 40, amount: 99_000m, memberId: Guid.NewGuid()));
        _config.Stages = [new DunningStage(1, "Unica", "{monto} / {dias}")];

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.WhatsAppUrl.Should().EndWith(Uri.EscapeDataString("$10.000 / 20"));
    }

    [Fact]
    public async Task ConUnaDeudaMasNuevaQueLaPrimeraEtapa_UsaElTextoDeLaPrimera_SinEtapaEnElLog()
    {
        HaveOverdue(ChargeDue(daysOverdue: 2));
        _config.Stages = [new DunningStage(5, "Primera", "Texto de la primera"), new DunningStage(9, "Segunda", "Otro")];

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.WhatsAppUrl.Should().EndWith(Uri.EscapeDataString("Texto de la primera"));
        await _log.Received(1).AppendAsync(
            Arg.Is<NotificationLog>(n => n.StageDaysOverdue == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FuncionaConElCobroAutomaticoApagado()
    {
        _config.Enabled = false;
        HaveOverdue(ChargeDue(daysOverdue: 7));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.WhatsAppUrl.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sin celular")]
    public async Task SinCelularUsable_NoDevuelveUrl_NiRegistraNada(string? mobile)
    {
        HaveMember(mobile);
        HaveOverdue(ChargeDue(daysOverdue: 7));

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.PhoneUsable.Should().BeFalse();
        result.WhatsAppUrl.Should().BeNull();
        result.Reason.Should().Contain("celular");
        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
        await _log.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinDeudaVencida_NoDevuelveUrl_NiRegistraNada()
    {
        var settled = ChargeDue(daysOverdue: 7, amount: 1_000m);
        settled.ApplyPayment(1_000m);
        HaveOverdue(settled);

        var result = await Handler().Handle(Command(), CancellationToken.None);

        result.PhoneUsable.Should().BeTrue();
        result.WhatsAppUrl.Should().BeNull();
        result.Reason.Should().Contain("deuda");
        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SocioDeOtraSedeOEmpresa_LanzaNoEncontrado()
    {
        var foreign = Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Zoe", "Paz", DocumentType.DNI, "40111222", Gender.Female);
        _members.GetByIdAsync(foreign.Id, Arg.Any<CancellationToken>()).Returns(foreign);

        var act = () => Handler().Handle(new(_company.Id, _siteId, foreign.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _log.DidNotReceive().AppendAsync(Arg.Any<NotificationLog>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SocioInexistente_LanzaNoEncontrado()
    {
        var act = () => Handler().Handle(new(_company.Id, _siteId, Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
