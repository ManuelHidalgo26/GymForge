using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Charges;
using GymForge.Application.UseCases.Dunning;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Collections;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Pantalla de morosos: lista por sede, recordatorio por WhatsApp y cobro con el modal de pago.</summary>
public class CollectionsViewModelTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly IWhatsAppLinkSender _whatsApp = Substitute.For<IWhatsAppLinkSender>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private SessionContext _session = null!;
    private Site _site = null!;

    private async Task<CollectionsViewModel> SutAsync()
    {
        _site = Site.Create(_company.Id, "Central", "Calle 1");
        _session = await TestSession.InitializedAsync(_company, [_site]);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(Today);

        return new CollectionsViewModel(
            _mediator, _calendar, _whatsApp, _session, new ReceiptService(Substitute.For<IServiceScopeFactory>()));
    }

    private static OverdueMemberDto Debtor(
        string name, bool usableMobile = true, int days = 10, decimal total = 35_000m, DateTime? lastReminder = null) =>
        new(Guid.NewGuid(), name, usableMobile ? "11 2345-6789" : null, usableMobile,
            total, Today.AddDays(-days), days, ChargeCount: 1, "Recordatorio", lastReminder);

    private void HaveDebtors(params OverdueMemberDto[] debtors) =>
        _mediator.Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>())
            .Returns(debtors.ToList());

    private void RemindReturns(SendWhatsAppReminderResult result) =>
        _mediator.Send(Arg.Any<SendWhatsAppReminderCommand>(), Arg.Any<CancellationToken>()).Returns(result);

    // ── Carga ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Load_PideLaMoraDeLaSedeActivaConElDiaLocalDeLaEmpresa_YArmaLasFilas()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García", total: 35_000m);
        var beto = Debtor("Beto Paz", usableMobile: false, total: 20_000m);
        HaveDebtors(ana, beto);

        await sut.LoadCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<GetOverdueMembersQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.AsOf == Today),
            Arg.Any<CancellationToken>());
        sut.Rows.Select(r => r.FullName).Should().Equal("Ana García", "Beto Paz");
        sut.Rows[1].MobileText.Should().Be("Sin celular válido");
        sut.TotalOutstanding.Should().Be(55_000m);
        sut.DebtorCount.Should().Be(2);
        sut.IsEmpty.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Load_SinMorosos_MuestraElEstadoVacio()
    {
        var sut = await SutAsync();
        HaveDebtors();

        await sut.LoadCommand.ExecuteAsync(null);

        sut.Rows.Should().BeEmpty();
        sut.IsEmpty.Should().BeTrue();
        sut.TotalOutstanding.Should().Be(0m);
    }

    [Fact]
    public async Task Load_SiFalla_MuestraElMensaje_SinLanzar()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("base bloqueada"));

        await sut.LoadCommand.ExecuteAsync(null);

        sut.Message.Should().Contain("base bloqueada");
        sut.IsLoading.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "Hoy")]
    [InlineData(1, "Ayer")]
    [InlineData(5, "Hace 5 días")]
    public async Task Load_ElUltimoRecordatorioSeMuestraRelativoAHoy(int daysAgo, string expected)
    {
        var sut = await SutAsync();
        var today = DateOnly.FromDateTime(DateTime.Now);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(today);
        HaveDebtors(Debtor("Ana García", lastReminder: DateTime.UtcNow.AddDays(-daysAgo)));

        await sut.LoadCommand.ExecuteAsync(null);

        sut.Rows.Single().LastReminderText.Should().Be(expected);
    }

    [Fact]
    public async Task Load_SinRecordatoriosPrevios_LoDice()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));

        await sut.LoadCommand.ExecuteAsync(null);

        sut.Rows.Single().LastReminderText.Should().Be("Sin recordatorios");
    }

    // ── Recordatorio por WhatsApp ────────────────────────────────────────────

    [Fact]
    public async Task RemindWhatsApp_EnviaElComando_YAbreElLink()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García");
        HaveDebtors(ana);
        await sut.LoadCommand.ExecuteAsync(null);
        RemindReturns(new SendWhatsAppReminderResult(true, "https://wa.me/5491123456789?text=hola", null));
        _whatsApp.Open("https://wa.me/5491123456789?text=hola").Returns(true);

        await sut.RemindWhatsAppCommand.ExecuteAsync(sut.Rows.Single());

        await _mediator.Received(1).Send(
            Arg.Is<SendWhatsAppReminderCommand>(c =>
                c.CompanyId == _company.Id && c.SiteId == _site.Id && c.MemberId == ana.MemberId),
            Arg.Any<CancellationToken>());
        _whatsApp.Received(1).Open("https://wa.me/5491123456789?text=hola");
        sut.Message.Should().BeNull();
    }

    [Fact]
    public async Task RemindWhatsApp_RefrescaElUltimoRecordatorioDeLaFila()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García");
        HaveDebtors(ana);
        await sut.LoadCommand.ExecuteAsync(null);
        RemindReturns(new SendWhatsAppReminderResult(true, "https://wa.me/5491123456789?text=hola", null));
        _whatsApp.Open(Arg.Any<string>()).Returns(true);
        _mediator.ClearReceivedCalls();
        HaveDebtors(ana with { LastReminderAt = DateTime.UtcNow });   // lo que devolvería la lista recargada

        await sut.RemindWhatsAppCommand.ExecuteAsync(sut.Rows.Single());

        await _mediator.Received(1).Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>());
        sut.Rows.Single().LastReminderText.Should().Be("Hoy");
    }

    [Fact]
    public async Task RemindWhatsApp_SiNoHayLink_MuestraElMotivo_YNoAbreNada()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));
        await sut.LoadCommand.ExecuteAsync(null);
        RemindReturns(new SendWhatsAppReminderResult(true, null, "El socio no tiene deuda vencida."));

        await sut.RemindWhatsAppCommand.ExecuteAsync(sut.Rows.Single());

        sut.Message.Should().Be("El socio no tiene deuda vencida.");
        _whatsApp.DidNotReceive().Open(Arg.Any<string>());
    }

    [Fact]
    public async Task RemindWhatsApp_SiNoSePudoAbrirWhatsApp_Avisa()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));
        await sut.LoadCommand.ExecuteAsync(null);
        RemindReturns(new SendWhatsAppReminderResult(true, "https://wa.me/5491123456789?text=hola", null));
        _whatsApp.Open(Arg.Any<string>()).Returns(false);

        await sut.RemindWhatsAppCommand.ExecuteAsync(sut.Rows.Single());

        sut.Message.Should().Contain("WhatsApp");
    }

    [Fact]
    public async Task RemindWhatsApp_SiElComandoFalla_MuestraElError_SinLanzar()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));
        await sut.LoadCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<SendWhatsAppReminderCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("El socio no existe en esta sede."));

        await sut.RemindWhatsAppCommand.ExecuteAsync(sut.Rows.Single());

        sut.Message.Should().Contain("El socio no existe en esta sede.");
        _whatsApp.DidNotReceive().Open(Arg.Any<string>());
    }

    [Fact]
    public async Task RemindWhatsApp_EstaDeshabilitadoSinCelularUsable()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"), Debtor("Beto Paz", usableMobile: false));
        await sut.LoadCommand.ExecuteAsync(null);

        sut.RemindWhatsAppCommand.CanExecute(sut.Rows[0]).Should().BeTrue();
        sut.RemindWhatsAppCommand.CanExecute(sut.Rows[1]).Should().BeFalse();
        sut.RemindWhatsAppCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Cobro ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Charge_SinTurnoAbierto_AvisaQueHayQueAbrirLaCaja_YNoAbreElModal()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));
        await sut.LoadCommand.ExecuteAsync(null);

        sut.ChargeCommand.Execute(sut.Rows.Single());

        sut.Message.Should().Be("Abrí la caja para cobrar.");
        sut.IsPaymentModalOpen.Should().BeFalse();
        sut.PaymentModal.Should().BeNull();
    }

    [Fact]
    public async Task Charge_ConTurnoAbierto_AbreElModalDeCobroParaEseSocio_ConSuDeudaTotal()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García", total: 35_000m);
        HaveDebtors(ana);
        await sut.LoadCommand.ExecuteAsync(null);
        _session.SetOpenShift(Guid.NewGuid());

        sut.ChargeCommand.Execute(sut.Rows.Single());

        sut.IsPaymentModalOpen.Should().BeTrue();
        sut.PaymentModal!.MemberName.Should().Be("Ana García");
        sut.PaymentModal.Amount.Should().Be(35_000m);
        sut.Message.Should().BeNull();
    }

    [Fact]
    public async Task Charge_ElModalCobraAlSocioElegido()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García", total: 35_000m);
        HaveDebtors(ana);
        await sut.LoadCommand.ExecuteAsync(null);
        _session.SetOpenShift(Guid.NewGuid());
        _mediator.Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentDto(Guid.NewGuid(), ana.MemberId, 35_000m, PaymentMethod.Cash,
                DateTime.UtcNow, PaymentStatus.Completed, null, null, []));
        sut.ChargeCommand.Execute(sut.Rows.Single());

        await sut.PaymentModal!.ConfirmCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<ProcessPaymentCommand>(c =>
                c.MemberId == ana.MemberId && c.Amount == 35_000m && c.ChargeIds == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Charge_TrasCobrar_CierraElModal_YRecargaLaLista()
    {
        var sut = await SutAsync();
        var ana = Debtor("Ana García", total: 35_000m);
        HaveDebtors(ana);
        await sut.LoadCommand.ExecuteAsync(null);
        _session.SetOpenShift(Guid.NewGuid());
        _mediator.Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentDto(Guid.NewGuid(), ana.MemberId, 35_000m, PaymentMethod.Cash,
                DateTime.UtcNow, PaymentStatus.Completed, null, null, []));
        sut.ChargeCommand.Execute(sut.Rows.Single());
        _mediator.ClearReceivedCalls();
        HaveDebtors();   // ya no debe nada

        await sut.PaymentModal!.ConfirmCommand.ExecuteAsync(null);

        sut.IsPaymentModalOpen.Should().BeFalse();
        sut.PaymentModal.Should().BeNull();
        await _mediator.Received(1).Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>());
        sut.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Charge_CancelarElModal_LoCierraSinCobrarNiRecargar()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García"));
        await sut.LoadCommand.ExecuteAsync(null);
        _session.SetOpenShift(Guid.NewGuid());
        sut.ChargeCommand.Execute(sut.Rows.Single());
        _mediator.ClearReceivedCalls();

        sut.PaymentModal!.CancelCommand.Execute(null);

        sut.IsPaymentModalOpen.Should().BeFalse();
        sut.PaymentModal.Should().BeNull();
        await _mediator.DidNotReceive().Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>());
        await _mediator.DidNotReceive().Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>());
    }
}
