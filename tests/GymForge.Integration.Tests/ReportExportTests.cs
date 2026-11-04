using System.Text;
using ClosedXML.Excel;
using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Services;

namespace GymForge.Integration.Tests;

/// <summary>Exportador genérico de reportes (ADR-007, Pieza B): PDF con QuestPDF y Excel con ClosedXML.</summary>
public class ReportExportTests
{
    private static readonly ReportBranding Branding =
        new("Iron Temple SRL", "30-71234567-8", "Centro", LogoPath: null, BrandColorHex: "#10B981");

    private static readonly IReadOnlyList<ReportColumn> MoneyColumns =
    [
        new("Método", ReportColumnType.Text),
        new("Monto", ReportColumnType.Money),
        new("Pagos", ReportColumnType.Integer),
        new("%", ReportColumnType.Percent),
    ];

    private static ReportTable MethodsTable() => new(
        "Por método",
        MoneyColumns,
        [
            ["Efectivo", 1_234.50m, 3, 0.42m],
            ["Transferencia", 1_700m, 2, 0.58m],
        ],
        Totals: ["Total", 2_934.50m, 5, 1m]);

    private static ReportTable DatesTable() => new(
        "Morosos",
        [
            new ReportColumn("Socio", ReportColumnType.Text),
            new ReportColumn("Vence desde", ReportColumnType.Date),
            new ReportColumn("Último aviso", ReportColumnType.Date),
        ],
        [
            ["Ana García", new DateOnly(2026, 6, 1), new DateTime(2026, 6, 20, 15, 45, 0)],
            ["Beto Paz", new DateOnly(2026, 5, 10), null],
        ]);

    private static ReportDocument Document(params ReportTable[] tables) => new(
        Branding,
        "Recaudación desagregada",
        "Sede Centro · 01/11/2026 a 03/11/2026",
        new DateTime(2026, 11, 3, 9, 30, 0),
        tables.Length > 0 ? tables : [MethodsTable(), DatesTable()],
        FileStem: "recaudacion-desagregada-2026-11-01_a_2026-11-03");

    private static bool IsPdf(byte[] bytes) => Encoding.ASCII.GetString(bytes, 0, 5) == "%PDF-";

    private static XLWorkbook Open(byte[] xlsx) => new(new MemoryStream(xlsx));

    // ── XLSX: ida y vuelta (ClosedXML lee lo que escribe) ────────────────────────

    [Fact]
    public void Xlsx_UnaHojaPorTabla_ConElNombreDeLaTabla()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));

        book.Worksheets.Select(w => w.Name).Should().Equal("Por método", "Morosos");
    }

    [Fact]
    public void Xlsx_LosEncabezadosDeColumnaYLaMarcaEstanEnLaHoja()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));
        var sheet = book.Worksheet("Por método");

        sheet.Cell("A1").GetString().Should().Be("Iron Temple SRL");
        sheet.Cell("A2").GetString().Should().Contain("Recaudación desagregada").And.Contain("Sede Centro");
        var headerRow = sheet.Row(4);
        headerRow.Cell(1).GetString().Should().Be("Método");
        headerRow.Cell(2).GetString().Should().Be("Monto");
        headerRow.Cell(3).GetString().Should().Be("Pagos");
        headerRow.Cell(4).GetString().Should().Be("%");
    }

    [Fact]
    public void Xlsx_LaCeldaDeDineroEsNumericaConElValorExactoYFormatoDeMoneda()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));
        var money = book.Worksheet("Por método").Cell(5, 2);

        money.DataType.Should().Be(XLDataType.Number);
        money.GetDouble().Should().Be(1234.50);
        money.Style.NumberFormat.Format.Should().Contain("$").And.Contain("0.00");
    }

    [Fact]
    public void Xlsx_EnteroYPorcentajeSonNumericosConSuFormato()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));
        var sheet = book.Worksheet("Por método");

        sheet.Cell(5, 3).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(5, 3).GetDouble().Should().Be(3);
        sheet.Cell(5, 4).DataType.Should().Be(XLDataType.Number);
        sheet.Cell(5, 4).GetDouble().Should().Be(0.42);
        sheet.Cell(5, 4).Style.NumberFormat.Format.Should().Contain("%");
    }

    [Fact]
    public void Xlsx_LaFilaDeTotalesVaDespuesDeLasFilas_EnNegrita()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));
        var sheet = book.Worksheet("Por método");

        sheet.Cell(7, 1).GetString().Should().Be("Total");
        sheet.Cell(7, 2).GetDouble().Should().Be(2934.50);
        sheet.Cell(7, 1).Style.Font.Bold.Should().BeTrue();
    }

    [Fact]
    public void Xlsx_LasFechasSonCeldasDeFecha_YLaCeldaNulaQuedaVacia()
    {
        using var book = Open(new ReportXlsxGenerator().Generate(Document()));
        var sheet = book.Worksheet("Morosos");

        sheet.Cell(5, 2).DataType.Should().Be(XLDataType.DateTime);
        sheet.Cell(5, 2).GetDateTime().Should().Be(new DateTime(2026, 6, 1));
        sheet.Cell(5, 3).GetDateTime().Should().Be(new DateTime(2026, 6, 20, 15, 45, 0));
        sheet.Cell(5, 3).Style.NumberFormat.Format.Should().Contain("hh:mm");
        sheet.Cell(6, 3).IsEmpty().Should().BeTrue();
    }

    [Fact]
    public void Xlsx_LaNotaVaAlPieDeCadaHoja()
    {
        var document = Document(DatesTable()) with { Note = "Activos es una foto de hoy." };

        using var book = Open(new ReportXlsxGenerator().Generate(document));
        var sheet = book.Worksheet("Morosos");

        sheet.CellsUsed().Select(c => c.GetString()).Should().Contain("Activos es una foto de hoy.");
    }

    [Fact]
    public void Xlsx_UnTextoQueEmpiezaConIgualQuedaComoTexto_NoComoFormula()
    {
        var table = new ReportTable("Texto", [new ReportColumn("Socio", ReportColumnType.Text)], [["=1+1"]]);

        using var book = Open(new ReportXlsxGenerator().Generate(Document(table)));
        var cell = book.Worksheet("Texto").Cell(5, 1);

        cell.DataType.Should().Be(XLDataType.Text);
        cell.HasFormula.Should().BeFalse();
        cell.GetString().Should().Be("=1+1");
    }

    [Fact]
    public void Xlsx_TitulosInvalidosOQueSeRepiten_DanNombresDeHojaValidosYUnicos()
    {
        var longTitle = new string('A', 40);
        var tables = new[]
        {
            new ReportTable("Altas/Bajas: [2026]?", MoneyColumns, []),
            new ReportTable(longTitle, MoneyColumns, []),
            new ReportTable(longTitle, MoneyColumns, []),
        };

        using var book = Open(new ReportXlsxGenerator().Generate(Document(tables)));

        var names = book.Worksheets.Select(w => w.Name).ToList();
        names.Select(n => n.ToUpperInvariant()).Should().OnlyHaveUniqueItems();
        names.Should().OnlyContain(n => n.Length <= 31 && n.IndexOfAny(InvalidSheetChars) < 0);
        names.Should().HaveCount(3);
    }

    [Fact]
    public void Xlsx_ConTablaSinFilas_GeneraSoloElEncabezado()
    {
        var empty = new ReportTable("Vacía", MoneyColumns, []);

        using var book = Open(new ReportXlsxGenerator().Generate(Document(empty)));

        book.Worksheet("Vacía").Cell(4, 1).GetString().Should().Be("Método");
        book.Worksheet("Vacía").Cell(5, 1).IsEmpty().Should().BeTrue();
    }

    // ── PDF ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Pdf_ProduceUnPdfValido()
    {
        var bytes = new ReportPdfGenerator().Generate(Document());

        bytes.Should().NotBeNullOrEmpty();
        IsPdf(bytes).Should().BeTrue();
    }

    [Fact]
    public void Pdf_NoLanza_ConLogoAusenteColorInvalidoYSinCuit()
    {
        var branding = new ReportBranding("Gym", GymTaxId: null, SiteName: null,
            LogoPath: @"C:\no\existe\logo.png", BrandColorHex: "rojo");

        var bytes = new ReportPdfGenerator().Generate(Document() with { Branding = branding });

        IsPdf(bytes).Should().BeTrue();
    }

    [Fact]
    public void Pdf_ConLogoReal_LoIncluye()
    {
        var logo = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(logo, OnePixelPng);
        try
        {
            var branding = Branding with { LogoPath = logo };

            IsPdf(new ReportPdfGenerator().Generate(Document() with { Branding = branding })).Should().BeTrue();
        }
        finally { File.Delete(logo); }
    }

    [Fact]
    public void Pdf_ConUnLogoCorrupto_IgualGeneraElReporte()
    {
        var logo = Path.Combine(Path.GetTempPath(), $"logo-{Guid.NewGuid():N}.png");
        File.WriteAllText(logo, "esto no es una imagen");
        try
        {
            var branding = Branding with { LogoPath = logo };

            IsPdf(new ReportPdfGenerator().Generate(Document() with { Branding = branding })).Should().BeTrue();
        }
        finally { File.Delete(logo); }
    }

    [Fact]
    public void Pdf_NoLanza_ConTablaVacia_CeldasNulasYConOSinTotales()
    {
        var empty = new ReportTable("Vacía", MoneyColumns, []);
        var emptyWithTotals = empty with { Totals = ["Total", 0m, 0, null] };
        var withNulls = DatesTable();
        var generator = new ReportPdfGenerator();

        IsPdf(generator.Generate(Document(empty))).Should().BeTrue();
        IsPdf(generator.Generate(Document(emptyWithTotals))).Should().BeTrue();
        IsPdf(generator.Generate(Document(withNulls))).Should().BeTrue();
        IsPdf(generator.Generate(Document(MethodsTable()) with { Note = "Activos es una foto de hoy." })).Should().BeTrue();
    }

    [Fact]
    public void Pdf_ConMuchasFilas_PaginaSinLanzar()
    {
        var rows = Enumerable.Range(1, 400)
            .Select(i => (IReadOnlyList<object?>)["Socio " + i, i * 10m, i, 0.0025m])
            .ToList();

        var bytes = new ReportPdfGenerator().Generate(Document(new ReportTable("Largo", MoneyColumns, rows)));

        IsPdf(bytes).Should().BeTrue();
    }

    // ── ReportExporter ───────────────────────────────────────────────────────────

    [Fact]
    public void Exporter_DevuelveLaExtensionDeCadaFormato()
    {
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        exporter.Extension(ReportFormat.Pdf).Should().Be("pdf");
        exporter.Extension(ReportFormat.Xlsx).Should().Be("xlsx");
    }

    [Fact]
    public void Exporter_DespachaAlGeneradorDelFormatoPedido()
    {
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        IsPdf(exporter.Export(Document(), ReportFormat.Pdf)).Should().BeTrue();
        using var book = Open(exporter.Export(Document(), ReportFormat.Xlsx));
        book.Worksheets.Count.Should().Be(2);
    }

    [Fact]
    public void Exporter_ConUnFormatoDesconocido_Lanza()
    {
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        var act = () => exporter.Export(Document(), (ReportFormat)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Exporter_UnaFilaConOtraCantidadDeCeldasQueColumnas_Lanza()
    {
        var broken = new ReportTable("Rota", MoneyColumns, [["Efectivo", 100m]]);
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        var act = () => exporter.Export(Document(broken), ReportFormat.Xlsx);

        act.Should().Throw<ArgumentException>().WithMessage("*Rota*");
    }

    [Fact]
    public void Exporter_UnReporteSinTablas_Lanza()
    {
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());
        var empty = Document() with { Tables = [] };

        var act = () => exporter.Export(empty, ReportFormat.Pdf);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Exporter_UnaCeldaDeTipoIncorrecto_Lanza_EnLugarDeImprimirBasura()
    {
        var wrongType = new ReportTable("Mal", MoneyColumns, [["Efectivo", "cien", 1, 0.5m]]);
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        var xlsx = () => exporter.Export(Document(wrongType), ReportFormat.Xlsx);
        var pdf = () => exporter.Export(Document(wrongType), ReportFormat.Pdf);

        xlsx.Should().Throw<InvalidOperationException>().WithMessage("*Monto*");
        pdf.Should().Throw<InvalidOperationException>().WithMessage("*Monto*");
    }

    // ── Formato (única fuente: es-AR) ────────────────────────────────────────────

    [Theory]
    [InlineData(1234.5, "$1.234,50")]
    [InlineData(35000, "$35.000,00")]
    [InlineData(0, "$0,00")]
    [InlineData(-500.1, "-$500,10")]
    public void Formatting_Money_EsPesosArgentinos(double amount, string expected) =>
        ReportFormatting.Money((decimal)amount).Should().Be(expected);

    [Theory]
    [InlineData(0.42, "42%")]
    [InlineData(1, "100%")]
    [InlineData(0, "0%")]
    [InlineData(0.425, "42,5%")]
    public void Formatting_Percent_EsFraccionConSimbolo(double fraction, string expected) =>
        ReportFormatting.Percent((decimal)fraction).Should().Be(expected);

    [Fact]
    public void Formatting_Cell_FormateaCadaTipoYMarcaLosNulos()
    {
        ReportFormatting.Cell(new("x", ReportColumnType.Text), "Ana").Should().Be("Ana");
        ReportFormatting.Cell(new("x", ReportColumnType.Text), null).Should().Be("—");
        ReportFormatting.Cell(new("x", ReportColumnType.Money), 1234.5m).Should().Be("$1.234,50");
        ReportFormatting.Cell(new("x", ReportColumnType.Integer), 7).Should().Be("7");
        ReportFormatting.Cell(new("x", ReportColumnType.Date), new DateOnly(2026, 6, 1)).Should().Be("01/06/2026");
        ReportFormatting.Cell(new("x", ReportColumnType.Date), new DateTime(2026, 6, 1, 9, 5, 0))
            .Should().Be("01/06/2026 09:05");
        ReportFormatting.Cell(new("x", ReportColumnType.Percent), 0.42m).Should().Be("42%");
        ReportFormatting.Cell(new("x", ReportColumnType.Money), null).Should().Be("—");
    }

    [Theory]
    [InlineData("#10b981", "#10B981")]
    [InlineData("#10B981", "#10B981")]
    [InlineData("rojo", "#6366F1")]
    [InlineData("#12345", "#6366F1")]
    [InlineData("", "#6366F1")]
    [InlineData(null, "#6366F1")]
    public void Formatting_BrandColor_ValidaElHexOUsaElIndigoPorDefecto(string? hex, string expected) =>
        ReportFormatting.BrandColor(hex).Should().Be(expected);

    private static readonly char[] InvalidSheetChars = ['[', ']', ':', '*', '?', '/', '\\'];

    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    // ── Builders reales → exportador real ────────────────────────────────────

    [Fact]
    public void LosCincoReportesDeLosBuilders_SeExportanAPdfYAExcel()
    {
        var today = new DateOnly(2026, 11, 3);
        var from = new DateOnly(2026, 11, 1);
        var nowUtc = new DateTime(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
        var context = new ReportContext(Branding, "Sede Centro", "Argentina Standard Time");
        var payments = new PaymentsReport(35_000m, 1,
            [new ReportPaymentRow(Guid.NewGuid(), nowUtc.AddHours(-3), "Ana García", PaymentMethod.Cash, 35_000m)]);
        var breakdown = new RevenueBreakdown(
            100m,
            [new RevenueBreakdownRow("Efectivo", 60m, 2), new RevenueBreakdownRow("Transferencia", 40m, 1)],
            [new RevenueBreakdownRow("Mensual Full", 100m, 3)],
            [new RevenueBreakdownRow("Centro", 100m, 3)]);
        var debtors = new[]
        {
            new Application.UseCases.Dunning.OverdueMemberDto(
                Guid.NewGuid(), "Ana García", null, false, 40_000m, today.AddDays(-35), 35, 3, "Aviso", nowUtc.AddDays(-2)),
        };
        var flow = new MemberFlow(
            new Dictionary<MemberStatus, int> { [MemberStatus.Active] = 12, [MemberStatus.Frozen] = 1 },
            12,
            [new MemberFlowJoin("Beto Paz", "30111222", today)],
            [new MemberFlowLeave("Caro Gil", "Trimestral", today, null)],
            0);
        var commissions = new StaffCommissions(
            8_300m, [new StaffCommissionRow(Guid.NewGuid(), "Ana Paz", 5_000m, 3_300m, 8_300m)]);
        ReportDocument[] documents =
        [
            ReportBuilders.Payments(payments, from, today, context, nowUtc),
            ReportBuilders.Breakdown(breakdown, from, today, context, nowUtc),
            ReportBuilders.Overdue(debtors, today, context, nowUtc),
            ReportBuilders.Flow(flow, from, today, context, nowUtc),
            ReportBuilders.StaffCommissions(commissions, from, today, context, nowUtc),
        ];
        var exporter = new ReportExporter(new ReportPdfGenerator(), new ReportXlsxGenerator());

        foreach (var document in documents)
        {
            IsPdf(exporter.Export(document, ReportFormat.Pdf)).Should().BeTrue(document.Title);
            using var book = Open(exporter.Export(document, ReportFormat.Xlsx));
            book.Worksheets.Count.Should().Be(document.Tables.Count, document.Title);
        }
    }
}
