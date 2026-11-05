using System.IO.Compression;
using ClosedXML.Excel;
using FluentAssertions;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Reports;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Pruebas de qa sobre los reportes (ADR-007): el flujo real de Caja contra la desagregada, la inyección de
/// fórmulas en el Excel exportado y la licencia de la dependencia de fuentes de ClosedXML.
/// </summary>
public class ReportQaTests : IAsyncLifetime
{
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _site = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym QA", "30-11111111-1");
        _site = Site.Create(_company.Id, "Centro", "Calle 1");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddAsync(_site);
        await _db.SaveChangesAsync();
        _tenant.Set(_company.Id, _site.Id);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    // ── La desagregada con datos que produce la app de verdad ────────────────

    // Regresión de qa (B1): SellMembershipCommandHandler creaba el Charge SIN MembershipId, así que ConceptLabel nunca
    // llegaba al nombre del plan y toda la recaudación de cuotas caía en "Cuotas".
    [Fact]
    public async Task Breakdown_UnaMembresiaVendidaPorCaja_SeAtribuyeAlNombreDelPlan()
    {
        var member = Member.Create(_company.Id, _site.Id, "Ana", "Paz", DocumentType.DNI, "30111222", Gender.Female);
        var plan = MembershipType.Create(_company.Id, "Mensual Full", MembershipBasis.Renewal, 35_000m);
        await _db.Members.AddAsync(member);
        await _db.MembershipTypes.AddAsync(plan);
        await _db.SaveChangesAsync();

        var clock = new SystemClock();
        var sell = new SellMembershipCommandHandler(
            new MembershipTypeRepository(_db), new ChargeRepository(_db), new MembershipRepository(_db),
            new MemberRepository(_db), new PaymentRepository(_db), Substitute.For<ICashRegister>(), new StaffRepository(_db),
            TestServices.Calendar(_db, clock), TestServices.ArrearsRelief(_db, clock));
        await sell.Handle(
            new SellMembershipCommand(_company.Id, _site.Id, Guid.NewGuid(), null, member.Id, plan.Id, PaymentMethod.Cash),
            CancellationToken.None);
        _db.ChangeTracker.Clear();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var breakdown = await new GetRevenueBreakdownQueryHandler(new PaymentRepository(_db), new SiteRepository(_db))
            .Handle(new GetRevenueBreakdownQuery(_company.Id, null, today.AddDays(-1), today.AddDays(1)), CancellationToken.None);

        breakdown.Total.Should().Be(35_000m);
        breakdown.ByConcept.Should().ContainSingle(r => r.Label == "Mensual Full" && r.Amount == 35_000m,
            "la cuota se vendió con el plan 'Mensual Full': el corte por plan debe mostrarlo, no 'Cuotas'");
    }

    // Observación de qa (F4-D): el rango se armaba con el día UTC, no con el día del gimnasio (UTC-3). Un pago de las
    // 23:30 del 03/11 en Argentina es 02:30 UTC del 04/11: el reporte "03/11 a 03/11" lo dejaba afuera y, en cambio,
    // metía los de las 21:00 a 24:00 del 02/11. El pico de un gimnasio es justo de 18 a 22 h.
    [Fact]
    public async Task Payments_UnPagoDeLas2330LocalesEntraAlDiaLocalDelReporte()
    {
        var payment = Payment.Create(_company.Id, _site.Id, null, Guid.NewGuid(), 10_000m, PaymentMethod.Cash);
        _db.Entry(payment).Property(p => p.ReceivedAt).CurrentValue = new DateTime(2026, 11, 4, 2, 30, 0, DateTimeKind.Utc);
        await _db.Payments.AddAsync(payment);
        await _db.SaveChangesAsync();

        var report = await new GetPaymentsReportQueryHandler(new PaymentRepository(_db), new SiteRepository(_db))
            .Handle(new GetPaymentsReportQuery(_company.Id, _site.Id, new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3)),
                CancellationToken.None);

        report.Rows.Should().ContainSingle("el pago es del 03/11 a las 23:30 hora del gimnasio")
            .Which.ReceivedAt.Should().Be(new DateTime(2026, 11, 3, 23, 30, 0));
    }

    // ── Excel: inyección de fórmulas (OWASP A03, "CSV/formula injection") ────

    // Los nombres de socio vienen del import CSV (archivo ajeno): un nombre que empieza con = + - @ no debe quedar
    // como fórmula al abrir el .xlsx en Excel.
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\",\"x\")")]
    [InlineData("=cmd|' /C calc'!A0")]
    [InlineData("+SUM(1;1)")]
    [InlineData("-2+3")]
    [InlineData("@SUM(1)")]
    public void Xlsx_UnTextoQueEmpiezaConSignoDeFormula_SeGuardaComoTextoYNoComoFormula(string hostile)
    {
        var document = new ReportDocument(
            new ReportBranding(hostile, null, null, null, null),
            "Morosos", "Sede Centro", new DateTime(2026, 11, 3, 9, 0, 0),
            [new ReportTable(
                "Morosos",
                [new ReportColumn("Socio", ReportColumnType.Text), new ReportColumn("Etapa", ReportColumnType.Text)],
                [[hostile, hostile]])],
            "morosos-2026-11-03",
            Note: hostile);

        var bytes = new ReportXlsxGenerator().Generate(document);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        foreach (var address in new[] { "A1", "A5", "B5" })
        {
            var cell = sheet.Cell(address);
            cell.HasFormula.Should().BeFalse($"{address} no puede ser una fórmula");
            cell.DataType.Should().Be(XLDataType.Text, $"{address} es texto");
            cell.GetString().Should().Be(hostile);
        }

        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        reader.ReadToEnd().Should().NotContain("<f>").And.NotContain("<f ", "ninguna celda lleva un elemento de fórmula");
    }

    // ── Exportador: datos hostiles o enormes ─────────────────────────────────

    // El padrón entra por CSV (archivo ajeno) y el motivo de baja es texto libre: un nombre con caracteres de control,
    // un texto enorme o un emoji no pueden romper el export de TODO el reporte.
    public static TheoryData<string, string> HostileTexts => new()
    {
        { "control", "Juan\u0000 Pé\u0007rez\u000B\u001F" },
        { "emoji", "Ana \U0001F600 Paz" },
        { "rtl", "مرحبا بالعالم" },
        { "palabra-gigante", new string('X', 5_000) },
        { "texto-en-el-limite-de-excel", new string('Y', 32_767) },
        { "saltos-de-linea", "línea 1\r\nlínea 2\n\ttab" },
    };

    [Theory]
    [MemberData(nameof(HostileTexts))]
    public void Export_ConTextoHostilEnUnaCelda_NoLanzaEnNingunFormato(string caso, string text)
    {
        var document = new ReportDocument(
            new ReportBranding("Gym QA", "30-11111111-1", "Centro", null, null),
            "Altas y bajas", "Toda la empresa", new DateTime(2026, 11, 3, 9, 0, 0),
            [new ReportTable(
                "Bajas del período",
                [new ReportColumn("Socio", ReportColumnType.Text), new ReportColumn("Motivo", ReportColumnType.Text)],
                [[text, text]])],
            "altas-bajas-2026-11-03");
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        var pdf = () => exporter.Export(document, GymForge.Application.Interfaces.ReportFormat.Pdf);
        var xlsx = () => exporter.Export(document, GymForge.Application.Interfaces.ReportFormat.Xlsx);

        pdf.Should().NotThrow($"PDF con texto '{caso}'");
        xlsx.Should().NotThrow($"Excel con texto '{caso}'");
    }

    [Fact]
    public void Xlsx_UnTextoDeMasDe32767Caracteres_SeTruncaAlLimiteDeLaCeldaDeExcel()
    {
        var huge = new string('Z', 40_000);
        var document = new ReportDocument(
            new ReportBranding("Gym QA", null, null, null, null),
            "Altas y bajas", "Toda la empresa", new DateTime(2026, 11, 3, 9, 0, 0),
            [new ReportTable("Bajas", [new ReportColumn("Motivo", ReportColumnType.Text)], [[huge]])],
            "altas-bajas-2026-11-03",
            Note: huge);

        var bytes = new ReportXlsxGenerator().Generate(document);

        using var workbook = new XLWorkbook(new MemoryStream(bytes));
        var sheet = workbook.Worksheet(1);
        sheet.Cell("A5").GetString().Should().HaveLength(32_767);
        sheet.Cell("A7").GetString().Should().HaveLength(32_767, "la nota también va a una celda");
    }

    [Fact]
    public void Export_UnReporteGrande_PaginaElPdfYNoPierdeFilasEnElExcel()
    {
        const int rows = 3_000;
        var document = new ReportDocument(
            new ReportBranding("Gym QA", null, null, null, null),
            "Morosos", "Sede Centro", new DateTime(2026, 11, 3, 9, 0, 0),
            [new ReportTable(
                "Morosos",
                [new ReportColumn("Socio", ReportColumnType.Text), new ReportColumn("Deuda", ReportColumnType.Money),
                 new ReportColumn("Desde", ReportColumnType.Date)],
                Enumerable.Range(1, rows).Select(i => (IReadOnlyList<object?>)[$"Socio {i}", i * 10m, new DateOnly(2026, 1, 1)]).ToList(),
                Totals: ["Deudores", rows * 10m, null])],
            "morosos-2026-11-03");
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        var pdf = exporter.Export(document, GymForge.Application.Interfaces.ReportFormat.Pdf);
        var xlsx = exporter.Export(document, GymForge.Application.Interfaces.ReportFormat.Xlsx);

        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        using var workbook = new XLWorkbook(new MemoryStream(xlsx));
        var sheet = workbook.Worksheet(1);
        sheet.Cell(4 + rows, 1).GetString().Should().Be($"Socio {rows}");
        ((decimal)sheet.Cell(4 + rows + 1, 2).GetDouble()).Should().Be(rows * 10m, "la fila de totales cierra la hoja");
    }

    // ── Licencia de la dependencia ───────────────────────────────────────────

    // ClosedXML acepta SixLabors.Fonts [1.0.0, 3.0.0): a partir de la 2.0 la licencia pasa a "Split" (comercial).
    // NuGet resuelve hoy la 1.0.0 (el mínimo); si alguien la sube, este test avisa antes de distribuir.
    [Fact]
    public void Licencia_SixLaborsFontsSigueEnLaLineaApache1()
    {
        var version = typeof(SixLabors.Fonts.FontCollection).Assembly.GetName().Version!;

        version.Major.Should().Be(1, "SixLabors.Fonts 2.x tiene licencia comercial (Split License)");
    }
}
