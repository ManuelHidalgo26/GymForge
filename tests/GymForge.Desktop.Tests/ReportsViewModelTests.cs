using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Reports;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Reports;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Hub de Reportes (ADR-007, Pieza F): selector de reporte, alcance, carga y exportación.</summary>
public class ReportsViewModelTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateOnly MonthStart = new(2026, 11, 1);
    private static readonly DateTime NowUtc = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly IReportExportService _exporter = Substitute.For<IReportExportService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private SessionContext _session = null!;
    private Site _site = null!;

    private async Task<ReportsViewModel> SutAsync()
    {
        _site = Site.Create(_company.Id, "Central", "Calle 1");
        _session = await TestSession.InitializedAsync(_company, [_site]);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(Today);
        _clock.Now.Returns(NowUtc);
        _mediator.Send(Arg.Any<GetReportContextQuery>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            var siteId = ci.Arg<GetReportContextQuery>().SiteId;
            return new ReportContext(
                new ReportBranding("Power Gym", "30-71234567-3", siteId is null ? null : "Central", null, "#10B981"),
                siteId is null ? "Toda la empresa" : "Sede Central",
                "Argentina Standard Time");
        });
        _exporter.ExportAsync(Arg.Any<ReportDocument>(), Arg.Any<ReportFormat>(), Arg.Any<CancellationToken>())
            .Returns(@"C:\Users\dueno\Documents\reporte.pdf");

        PinIs(valid: true);
        PrimeEmptyReports();
        var sut = new ReportsViewModel(
            _mediator, _session, new ReceiptService(Substitute.For<IServiceScopeFactory>()),
            _calendar, _exporter, _clock);
        sut.From = MonthStart;
        sut.To = Today;
        _mediator.ClearReceivedCalls();
        return sut;
    }

    /// <summary>Respuestas vacías por defecto: cambiar el rango al armar el VM dispara una carga que no debe fallar.</summary>
    private void PrimeEmptyReports()
    {
        HavePayments(new PaymentsReport(0m, 0, []));
        HaveBreakdown(new RevenueBreakdown(0m, [], [], []));
        HaveDebtors();
        HaveFlow(new MemberFlow(new Dictionary<MemberStatus, int>(), 0, [], [], 0));
        HaveCommissions(new StaffCommissions(0m, []));
    }

    private static PaymentsReport SamplePayments() => new(35_000m, 1,
        [new ReportPaymentRow(Guid.NewGuid(), NowUtc.AddHours(-2), "Ana García", PaymentMethod.Cash, 35_000m)]);

    private static RevenueBreakdown SampleBreakdown() => new(
        100_000m,
        [new RevenueBreakdownRow("Efectivo", 60_000m, 3), new RevenueBreakdownRow("Transferencia", 40_000m, 1)],
        [new RevenueBreakdownRow("Mensual Full", 100_000m, 4)],
        [new RevenueBreakdownRow("Central", 100_000m, 4)]);

    private static OverdueMemberDto Debtor(string name, decimal total) =>
        new(Guid.NewGuid(), name, "11 2345-6789", true, total, Today.AddDays(-10), 10, 1, "Recordatorio", null);

    private static MemberFlow SampleFlow() => new(
        new Dictionary<MemberStatus, int>
        {
            [MemberStatus.Active] = 120, [MemberStatus.Frozen] = 7, [MemberStatus.Overdue] = 11, [MemberStatus.Suspended] = 2,
        },
        120,
        [new MemberFlowJoin("Ana García", "30111222", Today)],
        [new MemberFlowLeave("Luis Paz", "Trimestral", Today, "Se mudó")],
        0);

    private void HavePayments(PaymentsReport report) =>
        _mediator.Send(Arg.Any<GetPaymentsReportQuery>(), Arg.Any<CancellationToken>()).Returns(report);

    private void HaveBreakdown(RevenueBreakdown breakdown) =>
        _mediator.Send(Arg.Any<GetRevenueBreakdownQuery>(), Arg.Any<CancellationToken>()).Returns(breakdown);

    private void HaveDebtors(params OverdueMemberDto[] debtors) =>
        _mediator.Send(Arg.Any<GetOverdueMembersQuery>(), Arg.Any<CancellationToken>()).Returns(debtors.ToList());

    private void HaveFlow(MemberFlow flow) =>
        _mediator.Send(Arg.Any<GetMemberFlowQuery>(), Arg.Any<CancellationToken>()).Returns(flow);

    private void HaveCommissions(StaffCommissions commissions) =>
        _mediator.Send(Arg.Any<GetStaffCommissionsQuery>(), Arg.Any<CancellationToken>()).Returns(commissions);

    private static StaffCommissions SampleCommissions() => new(
        8_300m,
        [
            new StaffCommissionRow(Guid.NewGuid(), "Beto Ruiz", 2_000m, 1_300m, 3_300m),
            new StaffCommissionRow(Guid.NewGuid(), "Ana Paz", 5_000m, 0m, 5_000m),
        ]);

    private static ReportDocument SentDocument(IReportExportService exporter) =>
        (ReportDocument)exporter.ReceivedCalls().Single().GetArguments()[0]!;

    private void PinIs(bool valid) =>
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>(), Arg.Any<CancellationToken>())
            .Returns(valid ? new StaffDto(Guid.NewGuid(), "Admin", StaffRole.Admin, "#112233") : null);

    /// <summary>Exporta pasando por el PIN (por defecto el PIN es correcto).</summary>
    private static async Task ExportAsync(ReportsViewModel sut, ReportFormat format)
    {
        sut.ExportCommand.Execute(format);
        sut.ExportPin = "1234";
        await sut.ConfirmExportCommand.ExecuteAsync(null);
    }

    // ── Selector y carga ─────────────────────────────────────────────────────

    [Fact]
    public async Task Arranca_EnLaRecaudacionDelPeriodo_ConLosCincoReportesEnElSelector()
    {
        var sut = await SutAsync();

        sut.SelectedReport.Should().Be(ReportKind.Payments);
        ReportsViewModel.ReportKinds.Should().Equal(
            ReportKind.Payments, ReportKind.RevenueBreakdown, ReportKind.Overdue, ReportKind.MemberFlow,
            ReportKind.StaffCommissions);
        sut.UsesDateRange.Should().BeTrue();
        sut.SupportsScope.Should().BeFalse();
    }

    [Fact]
    public async Task Pagos_LoadPideLosPagosDeLaSedeActivaEnElRango_YArmaLasFilas()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());

        await sut.LoadCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<GetPaymentsReportQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.From == MonthStart && q.To == Today),
            Arg.Any<CancellationToken>());
        sut.Total.Should().Be(35_000m);
        sut.Count.Should().Be(1);
        sut.Rows.Should().ContainSingle();
        sut.IsEmpty.Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task CambiarElReporte_CargaElNuevo_Desagregada()
    {
        var sut = await SutAsync();
        HaveBreakdown(SampleBreakdown());

        sut.SelectedReport = ReportKind.RevenueBreakdown;

        await _mediator.Received(1).Send(
            Arg.Is<GetRevenueBreakdownQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.From == MonthStart && q.To == Today),
            Arg.Any<CancellationToken>());
        sut.BreakdownTotal.Should().Be(100_000m);
        sut.ByMethod.Select(l => (l.Label, l.Amount, l.Count, l.Share)).Should().Equal(
            ("Efectivo", 60_000m, 3, 0.6m), ("Transferencia", 40_000m, 1, 0.4m));
        sut.ByConcept.Should().ContainSingle(l => l.Label == "Mensual Full" && l.Share == 1m);
        sut.BySite.Should().ContainSingle(l => l.Label == "Central");
        sut.SupportsScope.Should().BeTrue();
    }

    [Fact]
    public async Task Desagregada_ConAlcanceDeEmpresa_PideTodasLasSedes_YRecargaAlAlternar()
    {
        var sut = await SutAsync();
        HaveBreakdown(SampleBreakdown());
        sut.SelectedReport = ReportKind.RevenueBreakdown;
        _mediator.ClearReceivedCalls();

        sut.CompanyWide = true;

        await _mediator.Received(1).Send(
            Arg.Is<GetRevenueBreakdownQuery>(q => q.CompanyId == _company.Id && q.SiteId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Morosos_UsaElDiaLocalDeLaEmpresa_YNoElRangoDeFechas()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García", 40_000m), Debtor("Beto Paz", 15_000m));

        sut.SelectedReport = ReportKind.Overdue;

        await _mediator.Received(1).Send(
            Arg.Is<GetOverdueMembersQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.AsOf == Today),
            Arg.Any<CancellationToken>());
        sut.Debtors.Should().HaveCount(2);
        sut.DebtTotal.Should().Be(55_000m);
        sut.UsesDateRange.Should().BeFalse();
        sut.SupportsScope.Should().BeFalse();
    }

    [Fact]
    public async Task AltasYBajas_CargaElFlujoYExponeLaFotoDeEstados()
    {
        var sut = await SutAsync();
        HaveFlow(SampleFlow());

        sut.SelectedReport = ReportKind.MemberFlow;

        await _mediator.Received(1).Send(
            Arg.Is<GetMemberFlowQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.From == MonthStart && q.To == Today),
            Arg.Any<CancellationToken>());
        sut.ActiveNow.Should().Be(120);
        sut.FrozenNow.Should().Be(7);
        sut.OverdueNow.Should().Be(11);
        sut.SuspendedNow.Should().Be(2);
        sut.Flow!.Joins.Should().ContainSingle();
        sut.Flow.Leaves.Should().ContainSingle();
        sut.SupportsScope.Should().BeTrue();
    }

    [Fact]
    public async Task Comisiones_CargaConElAlcanceElegido_YExponeLasFilasYElTotal()
    {
        var sut = await SutAsync();
        HaveCommissions(SampleCommissions());

        sut.SelectedReport = ReportKind.StaffCommissions;

        await _mediator.Received(1).Send(
            Arg.Is<GetStaffCommissionsQuery>(q =>
                q.CompanyId == _company.Id && q.SiteId == _site.Id && q.From == MonthStart && q.To == Today),
            Arg.Any<CancellationToken>());
        sut.IsCommissionsReport.Should().BeTrue();
        sut.CommissionRows.Select(r => r.StaffName).Should().Equal("Beto Ruiz", "Ana Paz");
        sut.CommissionsTotal.Should().Be(8_300m);
        sut.SupportsScope.Should().BeTrue();
        sut.UsesDateRange.Should().BeTrue();
        sut.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task Comisiones_ConAlcanceDeEmpresa_PideTodasLasSedes()
    {
        var sut = await SutAsync();
        sut.SelectedReport = ReportKind.StaffCommissions;
        _mediator.ClearReceivedCalls();

        sut.CompanyWide = true;

        await _mediator.Received(1).Send(
            Arg.Is<GetStaffCommissionsQuery>(q => q.CompanyId == _company.Id && q.SiteId == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Comisiones_SinVentasConComision_MuestraElEstadoVacio_ConSuPropioTexto()
    {
        var sut = await SutAsync();

        sut.SelectedReport = ReportKind.StaffCommissions;

        sut.IsEmpty.Should().BeTrue();
        sut.EmptyTitle.Should().Be("Sin comisiones en el período");
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse();
    }

    [Fact]
    public async Task CambiarElRango_RecargaElReporteActivo()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());

        sut.From = MonthStart.AddDays(1);

        await _mediator.Received(1).Send(
            Arg.Is<GetPaymentsReportQuery>(q => q.From == MonthStart.AddDays(1)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SinPagos_MuestraElEstadoVacio()
    {
        var sut = await SutAsync();
        HavePayments(new PaymentsReport(0m, 0, []));

        await sut.LoadCommand.ExecuteAsync(null);

        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task UnaFalla_MuestraElMensaje_VaciaLosDatos_YDeshabilitaElExport()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<GetPaymentsReportQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("disco lleno"));

        await sut.LoadCommand.ExecuteAsync(null);

        sut.Message.Should().Contain("No se pudo cargar").And.Contain("disco lleno");
        sut.Rows.Should().BeEmpty();
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse();
        sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task UnaCargaVieja_NoPisaElReporteQueElUsuarioYaCambio()
    {
        var sut = await SutAsync();
        var slowPayments = new TaskCompletionSource<PaymentsReport>();
        _mediator.Send(Arg.Any<GetPaymentsReportQuery>(), Arg.Any<CancellationToken>()).Returns(slowPayments.Task);
        var firstLoad = sut.LoadCommand.ExecuteAsync(null);
        HaveFlow(SampleFlow());
        sut.SelectedReport = ReportKind.MemberFlow;

        slowPayments.SetResult(SamplePayments());
        await firstLoad;

        sut.Rows.Should().BeEmpty("la carga de pagos llegó tarde: el reporte activo ya es otro");
        sut.IsLoading.Should().BeFalse();
        await ExportAsync(sut, ReportFormat.Pdf);
        SentDocument(_exporter).Title.Should().Be("Altas y bajas");
    }

    // ── Exportar ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ReportFormat.Pdf)]
    [InlineData(ReportFormat.Xlsx)]
    public async Task Exportar_Pagos_ArmaElDocumentoYLoMandaAlServicioConElFormatoPedido(ReportFormat format)
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);

        await ExportAsync(sut, format);

        await _exporter.Received(1).ExportAsync(Arg.Any<ReportDocument>(), format, Arg.Any<CancellationToken>());
        var document = SentDocument(_exporter);
        document.Title.Should().Be("Recaudación del período");
        document.Subtitle.Should().Be("Sede Central · 01/11/2026 a 03/11/2026");
        document.Tables.Should().ContainSingle().Which.Rows.Should().ContainSingle();
        await _mediator.Received(1).Send(
            Arg.Is<GetReportContextQuery>(q => q.CompanyId == _company.Id && q.SiteId == _site.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exportar_Desagregada_ConAlcanceDeEmpresa_NoLlevaSedeEnElEncabezado()
    {
        var sut = await SutAsync();
        HaveBreakdown(SampleBreakdown());
        sut.SelectedReport = ReportKind.RevenueBreakdown;
        sut.CompanyWide = true;

        await ExportAsync(sut, ReportFormat.Xlsx);

        var document = SentDocument(_exporter);
        document.Title.Should().Be("Recaudación desagregada");
        document.Subtitle.Should().StartWith("Toda la empresa");
        document.Branding.SiteName.Should().BeNull();
        document.Tables.Should().HaveCount(3);
        await _mediator.Received().Send(
            Arg.Is<GetReportContextQuery>(q => q.SiteId == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exportar_Morosos_UsaLaMismaFechaConLaQueSeCalculoLaMora()
    {
        var sut = await SutAsync();
        HaveDebtors(Debtor("Ana García", 40_000m));
        sut.SelectedReport = ReportKind.Overdue;

        await ExportAsync(sut, ReportFormat.Pdf);

        var document = SentDocument(_exporter);
        document.Title.Should().Be("Morosos");
        document.Subtitle.Should().Be("Sede Central · al 03/11/2026");
        document.Tables.Single().Totals![0].Should().Be("Deudores: 1");
    }

    [Fact]
    public async Task Exportar_AltasYBajas_LlevaLasTresTablasYLaNota()
    {
        var sut = await SutAsync();
        HaveFlow(SampleFlow());
        sut.SelectedReport = ReportKind.MemberFlow;

        await ExportAsync(sut, ReportFormat.Pdf);

        var document = SentDocument(_exporter);
        document.Title.Should().Be("Altas y bajas");
        document.Tables.Select(t => t.Title).Should().Equal("Resumen", "Altas del período", "Bajas del período");
        document.Note.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Exportar_Comisiones_ArmaLaTablaConLaFilaDeTotalesYLaNota()
    {
        var sut = await SutAsync();
        HaveCommissions(SampleCommissions());
        sut.SelectedReport = ReportKind.StaffCommissions;

        await ExportAsync(sut, ReportFormat.Xlsx);

        var document = SentDocument(_exporter);
        document.Title.Should().Be("Comisiones del staff");
        var table = document.Tables.Single();
        table.Rows.Should().HaveCount(2);
        table.Totals.Should().Equal("Total", 7_000m, 1_300m, 8_300m);
        document.Note.Should().Be(ReportBuilders.CommissionsNote);
        await _exporter.Received(1).ExportAsync(Arg.Any<ReportDocument>(), ReportFormat.Xlsx, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Exportar_EstaDeshabilitado_SinDatos_YAntesDeLaPrimeraCarga()
    {
        var sut = await SutAsync();

        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse("todavía no se cargó nada");

        HavePayments(new PaymentsReport(0m, 0, []));
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse("no hay pagos que exportar");

        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeTrue();
    }

    [Fact]
    public async Task Exportar_EstaDeshabilitado_MientrasCarga()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        var slow = new TaskCompletionSource<PaymentsReport>();
        _mediator.Send(Arg.Any<GetPaymentsReportQuery>(), Arg.Any<CancellationToken>()).Returns(slow.Task);

        var loading = sut.LoadCommand.ExecuteAsync(null);

        sut.IsLoading.Should().BeTrue();
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse();
        slow.SetResult(SamplePayments());
        await loading;
        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeTrue();
    }

    [Fact]
    public async Task CambiarDeReporte_DeshabilitaElExportHastaQueCargueElNuevo()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        var slow = new TaskCompletionSource<MemberFlow>();
        _mediator.Send(Arg.Any<GetMemberFlowQuery>(), Arg.Any<CancellationToken>()).Returns(slow.Task);

        sut.SelectedReport = ReportKind.MemberFlow;

        sut.ExportCommand.CanExecute(ReportFormat.Pdf).Should().BeFalse("el export todavía sería el de pagos");
        slow.SetResult(SampleFlow());
    }

    [Fact]
    public async Task Exportar_SiElUsuarioGuarda_MuestraDondeQuedo()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);

        await ExportAsync(sut, ReportFormat.Pdf);

        sut.ExportedPath.Should().Be(@"C:\Users\dueno\Documents\reporte.pdf");
        sut.Message.Should().BeNull();
    }

    [Fact]
    public async Task Exportar_PideElPin_YNoExportaHastaConfirmarlo()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);

        sut.ExportCommand.Execute(ReportFormat.Pdf);

        sut.IsExportPinNeeded.Should().BeTrue();
        await _exporter.DidNotReceiveWithAnyArgs().ExportAsync(default!, default, default);
    }

    [Fact]
    public async Task Exportar_ConPinIncorrecto_NoExporta_YDejaReintentar()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        PinIs(valid: false);

        await ExportAsync(sut, ReportFormat.Pdf);

        await _exporter.DidNotReceiveWithAnyArgs().ExportAsync(default!, default, default);
        sut.ExportPinError.Should().Be("PIN incorrecto.");
        sut.IsExportPinNeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Exportar_ConPinCorrecto_ExportaConElFormatoPedido_YCierraElModal()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);

        await ExportAsync(sut, ReportFormat.Xlsx);

        await _exporter.Received(1).ExportAsync(Arg.Any<ReportDocument>(), ReportFormat.Xlsx, Arg.Any<CancellationToken>());
        sut.ExportedPath.Should().Be(@"C:\Users\dueno\Documents\reporte.pdf");
        sut.IsExportPinNeeded.Should().BeFalse();
        sut.ExportPin.Should().BeEmpty();
    }

    [Fact]
    public async Task Exportar_CancelarElModal_LoCierraSinExportar()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.Execute(ReportFormat.Pdf);
        sut.ExportPin = "12";

        sut.CancelExportCommand.Execute(null);

        sut.IsExportPinNeeded.Should().BeFalse();
        sut.ExportPin.Should().BeEmpty();
        await _exporter.DidNotReceiveWithAnyArgs().ExportAsync(default!, default, default);
    }

    [Fact]
    public async Task Exportar_CambiarElAlcance_CierraElModalDelPin_YConfirmarSinReabrirNoExporta()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.Execute(ReportFormat.Pdf);
        sut.ExportPin = "1234";

        sut.CompanyWide = true;
        await sut.ConfirmExportCommand.ExecuteAsync(null);

        sut.IsExportPinNeeded.Should().BeFalse();
        await _exporter.DidNotReceiveWithAnyArgs().ExportAsync(default!, default, default);
    }

    [Fact]
    public async Task Exportar_AlArrancarUnaRecarga_CierraElModalDelPin_YConfirmarSinReabrirNoExporta()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.Execute(ReportFormat.Pdf);
        sut.ExportPin = "1234";

        sut.IsLoading = true;
        await sut.ConfirmExportCommand.ExecuteAsync(null);

        sut.IsExportPinNeeded.Should().BeFalse();
        await _exporter.DidNotReceiveWithAnyArgs().ExportAsync(default!, default, default);
    }

    [Fact]
    public async Task Exportar_CambiarDeReporte_CierraElModalDelPin()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        sut.ExportCommand.Execute(ReportFormat.Pdf);

        sut.SelectedReport = ReportKind.MemberFlow;

        sut.IsExportPinNeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Exportar_SiElUsuarioCancela_NoAvisaNada()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        _exporter.ExportAsync(Arg.Any<ReportDocument>(), Arg.Any<ReportFormat>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await ExportAsync(sut, ReportFormat.Pdf);

        sut.ExportedPath.Should().BeNull();
        sut.Message.Should().BeNull();
    }

    [Fact]
    public async Task Exportar_SiNoSePuedeEscribir_AvisaSinRomperLaVista()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        _exporter.ExportAsync(Arg.Any<ReportDocument>(), Arg.Any<ReportFormat>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("El archivo está abierto en otro programa."));

        await ExportAsync(sut, ReportFormat.Xlsx);

        sut.Message.Should().Contain("No se pudo exportar").And.Contain("abierto en otro programa");
        sut.ExportedPath.Should().BeNull();
    }

    [Fact]
    public async Task Recargar_DescartaLaRutaDelUltimoExport()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        await ExportAsync(sut, ReportFormat.Pdf);
        sut.ExportedPath.Should().NotBeNull();

        await sut.LoadCommand.ExecuteAsync(null);

        sut.ExportedPath.Should().BeNull();
    }

    [Fact]
    public async Task CambiarElRango_DescartaLaRutaDelUltimoExport()
    {
        var sut = await SutAsync();
        HavePayments(SamplePayments());
        await sut.LoadCommand.ExecuteAsync(null);
        await ExportAsync(sut, ReportFormat.Pdf);

        sut.From = MonthStart.AddDays(1);

        sut.ExportedPath.Should().BeNull();
    }

    [Fact]
    public async Task PrimeraCarga_ArrancaEnEsteMesSegunElDiaDelGimnasio()
    {
        var sut = NewUnprimed(await SessionAsync());

        await sut.LoadCommand.ExecuteAsync(null);

        sut.From.Should().Be(MonthStart);
        sut.To.Should().Be(Today);
    }

    [Fact]
    public async Task Atajos_UsanElDiaDelGimnasio()
    {
        var sut = await SutAsync();

        await sut.TodayCommand.ExecuteAsync(null);
        (sut.From, sut.To).Should().Be((Today, Today));

        await sut.ThisMonthCommand.ExecuteAsync(null);
        (sut.From, sut.To).Should().Be((MonthStart, Today));
    }

    private async Task<SessionContext> SessionAsync()
    {
        _site = Site.Create(_company.Id, "Central", "Calle 1");
        var session = await TestSession.InitializedAsync(_company, [_site]);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(Today);
        PrimeEmptyReports();
        return session;
    }

    private ReportsViewModel NewUnprimed(SessionContext session) => new(
        _mediator, session, new ReceiptService(Substitute.For<IServiceScopeFactory>()), _calendar, _exporter, _clock);
}
