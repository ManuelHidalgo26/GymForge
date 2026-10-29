using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class GetOverdueMembersQueryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid SiteId = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly INotificationLogRepository _log = Substitute.For<INotificationLogRepository>();
    private readonly DunningConfig _config = new();

    private GetOverdueMembersQueryHandler Handler() => new(_charges, _log, _config);

    private Task<IReadOnlyList<OverdueMemberDto>> RunAsync() =>
        Handler().Handle(new GetOverdueMembersQuery(CompanyId, SiteId, Today), CancellationToken.None);

    private static Member NewMember(string firstName, string? mobile)
    {
        var member = Member.Create(CompanyId, SiteId, firstName, "García", DocumentType.DNI,
            Guid.NewGuid().ToString("N")[..8], Gender.Female);
        member.UpdateContact(null, mobile);
        return member;
    }

    /// <summary>Cargo vencido del socio, con la navegación Member cargada como lo hace el Include del repositorio.</summary>
    private static Charge ChargeOf(Member member, int daysOverdue, decimal amount = 10_000m)
    {
        var charge = Charge.Create(CompanyId, SiteId, member.Id, null,
            ConceptType.MembershipFee, "Cuota", amount, Today.AddDays(-daysOverdue));
        typeof(Charge).GetProperty(nameof(Charge.Member))!.SetValue(charge, member);
        return charge;
    }

    private void HaveOverdue(params Charge[] charges) =>
        _charges.GetOverdueBySiteAsync(CompanyId, SiteId, Today, Arg.Any<CancellationToken>())
            .Returns(charges.ToList());

    [Fact]
    public async Task AgrupaLosCargosDeUnSocioEnUnaSolaFila()
    {
        var ana = NewMember("Ana", "11 2345-6789");
        HaveOverdue(ChargeOf(ana, 5, 10_000m), ChargeOf(ana, 35, 25_000m), ChargeOf(ana, 12, 5_000m));

        var rows = await RunAsync();

        var row = rows.Should().ContainSingle().Subject;
        row.MemberId.Should().Be(ana.Id);
        row.FullName.Should().Be("Ana García");
        row.Mobile.Should().Be("11 2345-6789");
        row.TotalOutstanding.Should().Be(40_000m);
        row.ChargeCount.Should().Be(3);
        row.OldestDueDate.Should().Be(Today.AddDays(-35));
        row.DaysOverdue.Should().Be(35);
    }

    [Fact]
    public async Task UnPagoParcialRestaDelTotal()
    {
        var ana = NewMember("Ana", "11 2345-6789");
        var charge = ChargeOf(ana, 10, 10_000m);
        charge.ApplyPayment(4_000m);
        HaveOverdue(charge);

        (await RunAsync()).Single().TotalOutstanding.Should().Be(6_000m);
    }

    [Fact]
    public async Task IgnoraLoQueYaNoEsMora()
    {
        var ana = NewMember("Ana", "11 2345-6789");
        var settled = ChargeOf(ana, 10, 10_000m);
        settled.ApplyPayment(10_000m);
        HaveOverdue(settled);

        (await RunAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PideLaMoraSoloDeLaSedeIndicada()
    {
        var ana = NewMember("Ana", "11 2345-6789");
        HaveOverdue(ChargeOf(ana, 10));
        var otherSiteId = Guid.NewGuid();
        _charges.GetOverdueBySiteAsync(CompanyId, otherSiteId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<Charge> { ChargeOf(NewMember("Beto", "11 1111-1111"), 99) });

        var rows = await RunAsync();

        rows.Select(r => r.FullName).Should().Equal("Ana García");
        await _charges.DidNotReceive().GetOverdueAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("11 2345-6789", true)]
    [InlineData("+54 9 11 2345 6789", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("sin celular", false)]
    public async Task MarcaSiElSocioTieneUnCelularUsable(string? mobile, bool expected)
    {
        HaveOverdue(ChargeOf(NewMember("Ana", mobile), 10));

        (await RunAsync()).Single().HasUsableMobile.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Aviso amable")]
    [InlineData(4, "Recordatorio")]
    [InlineData(8, "Firme")]
    [InlineData(20, "Urgente")]
    [InlineData(90, "Último aviso")]
    public async Task LaEtapaVigenteSaleDeLaAntiguedadDeLaDeudaMasVieja(int daysOverdue, string expectedLabel)
    {
        HaveOverdue(ChargeOf(NewMember("Ana", null), daysOverdue));

        (await RunAsync()).Single().CurrentStageLabel.Should().Be(expectedLabel);
    }

    [Fact]
    public async Task LaEtapaRespetaLaConfiguracionEditada_YAntesDeLaPrimeraMuestraLaPrimera()
    {
        _config.Stages = [new DunningStage(5, "Mi primera", "x"), new DunningStage(20, "Mi segunda", "y")];
        var reciente = NewMember("Ana", null);
        var vieja = NewMember("Beto", null);
        HaveOverdue(ChargeOf(reciente, 2), ChargeOf(vieja, 25));

        var rows = await RunAsync();

        rows.Single(r => r.MemberId == reciente.Id).CurrentStageLabel.Should().Be("Mi primera");
        rows.Single(r => r.MemberId == vieja.Id).CurrentStageLabel.Should().Be("Mi segunda");
    }

    [Fact]
    public async Task ElUltimoRecordatorioSaleDelLog_YEsNullSiNuncaSeAviso()
    {
        var avisada = NewMember("Ana", "11 2345-6789");
        var sinAviso = NewMember("Beto", "11 1111-1111");
        HaveOverdue(ChargeOf(avisada, 10), ChargeOf(sinAviso, 10));
        var reminder = NotificationLog.Manual(
            CompanyId, SiteId, avisada.Id, null, NotificationChannel.WhatsApp, 7, "11 2345-6789");
        _log.GetLastForMemberAsync(avisada.Id, Arg.Any<CancellationToken>()).Returns(reminder);

        var rows = await RunAsync();

        rows.Single(r => r.MemberId == avisada.Id).LastReminderAt.Should().Be(reminder.SentAt);
        rows.Single(r => r.MemberId == sinAviso.Id).LastReminderAt.Should().BeNull();
    }

    [Fact]
    public async Task OrdenaPorDiasDeAtrasoDescendente_ConElMontoComoDesempate()
    {
        var reciente = NewMember("Ana", null);
        var vieja = NewMember("Beto", null);
        var empateChica = NewMember("Carla", null);
        var empateGrande = NewMember("Dora", null);
        HaveOverdue(
            ChargeOf(reciente, 3), ChargeOf(vieja, 40),
            ChargeOf(empateChica, 10, 5_000m), ChargeOf(empateGrande, 10, 50_000m));

        var rows = await RunAsync();

        rows.Select(r => r.MemberId).Should().Equal(vieja.Id, empateGrande.Id, empateChica.Id, reciente.Id);
    }

    [Fact]
    public async Task SinMorosos_DevuelveVacio()
    {
        HaveOverdue();

        (await RunAsync()).Should().BeEmpty();
    }
}
