using FluentAssertions;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Reports;
using GymForge.Domain.Enums;

namespace GymForge.Application.Tests.Reports;

/// <summary>Armado puro de los documentos de reporte (ADR-007, Pieza F): columnas tipadas, totales y filas.</summary>
public class ReportBuildersTests
{
    // 03/11/2026 12:00 hora de Argentina (UTC-3).
    private static readonly DateTime NowUtc = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static ReportContext Context(string scope = "Sede Central", string? siteName = "Central") => new(
        new ReportBranding("Power Gym", "30-71234567-3", siteName, LogoPath: null, BrandColorHex: "#10B981"),
        scope,
        Timezone: "Argentina Standard Time");

    private static ReportTable Single(ReportDocument document) => document.Tables.Should().ContainSingle().Subject;

    // ── Morosos ──────────────────────────────────────────────────────────────

    private static OverdueMemberDto Debtor(
        string name, decimal total, int days, int charges = 1, string? mobile = "11 2345-6789",
        DateTime? lastReminderUtc = null) =>
        new(Guid.NewGuid(), name, mobile, HasUsableMobile: mobile is not null, total,
            Today.AddDays(-days), days, charges, "Recordatorio", lastReminderUtc);

    [Fact]
    public void Overdue_ArmaLasOchoColumnasTipadasDeLaSpec()
    {
        var document = ReportBuilders.Overdue([Debtor("Ana García", 35_000m, 12)], Today, Context(), NowUtc);

        Single(document).Columns.Should().Equal(
            new ReportColumn("Socio", ReportColumnType.Text),
            new ReportColumn("Celular", ReportColumnType.Text),
            new ReportColumn("Deuda", ReportColumnType.Money),
            new ReportColumn("Cargos", ReportColumnType.Integer),
            new ReportColumn("Vence desde", ReportColumnType.Date),
            new ReportColumn("Días de atraso", ReportColumnType.Integer),
            new ReportColumn("Etapa", ReportColumnType.Text),
            new ReportColumn("Último recordatorio", ReportColumnType.Date));
    }

    [Fact]
    public void Overdue_CadaFilaTraeLosValoresCrudosDelSocio_EnElOrdenDeLaQuery()
    {
        var reminder = new DateTime(2026, 11, 2, 1, 30, 0, DateTimeKind.Utc);   // 01/11 22:30 hora local
        var debtors = new[]
        {
            Debtor("Ana García", 40_000m, 35, charges: 3, lastReminderUtc: reminder),
            Debtor("Beto Paz", 20_000m, 5, mobile: null),
        };

        var rows = Single(ReportBuilders.Overdue(debtors, Today, Context(), NowUtc)).Rows;

        rows.Should().HaveCount(2);
        rows[0].Should().Equal("Ana García", "11 2345-6789", 40_000m, 3,
            Today.AddDays(-35), 35, "Recordatorio", new DateOnly(2026, 11, 1));
        rows[1].Should().Equal("Beto Paz", null, 20_000m, 1,
            Today.AddDays(-5), 5, "Recordatorio", null);
    }

    [Fact]
    public void Overdue_LosTotalesSumanLaDeudaYCuentanLosDeudores()
    {
        var debtors = new[] { Debtor("Ana", 40_000m, 35), Debtor("Beto", 20_000m, 5), Debtor("Caro", 5_500m, 1) };

        var totals = Single(ReportBuilders.Overdue(debtors, Today, Context(), NowUtc)).Totals;

        totals.Should().NotBeNull();
        totals![0].Should().Be("Deudores: 3");
        totals[2].Should().Be(65_500m);
    }

    [Fact]
    public void Overdue_SinMorosos_DaUnaTablaVaciaConTotalesEnCero()
    {
        var table = Single(ReportBuilders.Overdue([], Today, Context(), NowUtc));

        table.Rows.Should().BeEmpty();
        table.Totals![0].Should().Be("Deudores: 0");
        table.Totals[2].Should().Be(0m);
    }

    [Fact]
    public void Overdue_TituloSubtituloNombreDeArchivoYFechaDeGeneracionEnHoraLocal()
    {
        var document = ReportBuilders.Overdue([], Today, Context(), NowUtc);

        document.Title.Should().Be("Morosos");
        document.Subtitle.Should().Be("Sede Central · al 03/11/2026");
        document.FileStem.Should().Be("morosos-2026-11-03");
        document.GeneratedAt.Should().Be(new DateTime(2026, 11, 3, 12, 0, 0));
        document.Branding.GymName.Should().Be("Power Gym");
        document.Note.Should().BeNull();
    }

    // ── Pagos del período (A.1) ──────────────────────────────────────────────

    [Fact]
    public void Payments_ArmaLaTablaDePagosConFechaLocalYMetodoEnEspanol()
    {
        var cashAt = new DateTime(2026, 11, 2, 23, 30, 0);   // el reporte de pagos ya trae la hora del gimnasio
        var report = new PaymentsReport(55_000m, 2,
        [
            new ReportPaymentRow(Guid.NewGuid(), cashAt, "Ana García", PaymentMethod.Cash, 35_000m),
            new ReportPaymentRow(Guid.NewGuid(), cashAt.AddHours(10), "—", PaymentMethod.BankTransfer, 20_000m),
        ]);

        var document = ReportBuilders.Payments(report, new DateOnly(2026, 11, 1), Today, Context(), NowUtc);
        var table = Single(document);

        table.Title.Should().Be("Pagos del período");
        table.Columns.Should().Equal(
            new ReportColumn("Fecha", ReportColumnType.Date),
            new ReportColumn("Socio", ReportColumnType.Text),
            new ReportColumn("Método", ReportColumnType.Text),
            new ReportColumn("Monto", ReportColumnType.Money));
        table.Rows[0].Should().Equal(cashAt, "Ana García", "Efectivo", 35_000m);
        table.Rows[1].Should().Equal(cashAt.AddHours(10), "—", "Transferencia", 20_000m);
        table.Totals.Should().Equal(new object?[] { null, "Pagos: 2", null, 55_000m });
    }

    [Fact]
    public void Payments_TituloSubtituloYArchivoLlevanElRango()
    {
        var document = ReportBuilders.Payments(
            new PaymentsReport(0m, 0, []), new DateOnly(2026, 11, 1), Today, Context(), NowUtc);

        document.Title.Should().Be("Recaudación del período");
        document.Subtitle.Should().Be("Sede Central · 01/11/2026 a 03/11/2026");
        document.FileStem.Should().Be("recaudacion-del-periodo-2026-11-01_a_2026-11-03");
    }

    [Fact]
    public void Payments_ConUnSoloDia_ElSubtituloYElArchivoNoRepitenLaFecha()
    {
        var day = new DateOnly(2026, 11, 3);

        var document = ReportBuilders.Payments(new PaymentsReport(0m, 0, []), day, day, Context(), NowUtc);

        document.Subtitle.Should().Be("Sede Central · 03/11/2026");
        document.FileStem.Should().Be("recaudacion-del-periodo-2026-11-03");
    }

    // ── Recaudación desagregada (A.2) ────────────────────────────────────────

    private static RevenueBreakdown SampleBreakdown() => new(
        Total: 100_000m,
        ByMethod:
        [
            new RevenueBreakdownRow("Efectivo", 60_000m, 4),
            new RevenueBreakdownRow("Transferencia", 40_000m, 1),
        ],
        ByConcept:
        [
            new RevenueBreakdownRow("Mensual Full", 70_000m, 2),
            new RevenueBreakdownRow("Productos", 20_000m, 2),
            new RevenueBreakdownRow("A cuenta (saldo a favor)", 10_000m, 1),
        ],
        BySite:
        [
            new RevenueBreakdownRow("Centro", 75_000m, 3),
            new RevenueBreakdownRow("Norte", 25_000m, 2),
        ]);

    private static ReportDocument Breakdown(RevenueBreakdown? data = null) => ReportBuilders.Breakdown(
        data ?? SampleBreakdown(), new DateOnly(2026, 11, 1), Today, Context("Toda la empresa", siteName: null), NowUtc);

    [Fact]
    public void Breakdown_ArmaTresTablas_PorMetodoConceptoYSede()
    {
        var document = Breakdown();

        document.Tables.Select(t => t.Title).Should().Equal("Por método de pago", "Por concepto y plan", "Por sede");
        document.Tables[0].Columns.Select(c => (c.Header, c.Type)).Should().Equal(
            ("Método", ReportColumnType.Text), ("Monto", ReportColumnType.Money),
            ("Pagos", ReportColumnType.Integer), ("%", ReportColumnType.Percent));
        document.Tables[1].Columns.Select(c => (c.Header, c.Type)).Should().Equal(
            ("Concepto / Plan", ReportColumnType.Text), ("Monto", ReportColumnType.Money), ("%", ReportColumnType.Percent));
        document.Tables[2].Columns.Select(c => c.Header).Should().Equal("Sede", "Monto", "Pagos", "%");
    }

    [Fact]
    public void Breakdown_CadaFilaLlevaSuPorcentajeSobreElTotalDelCorte()
    {
        var document = Breakdown();

        document.Tables[0].Rows[0].Should().Equal("Efectivo", 60_000m, 4, 0.6m);
        document.Tables[0].Rows[1].Should().Equal("Transferencia", 40_000m, 1, 0.4m);
        document.Tables[1].Rows[2].Should().Equal("A cuenta (saldo a favor)", 10_000m, 0.1m);
        document.Tables[2].Rows[0].Should().Equal("Centro", 75_000m, 3, 0.75m);
    }

    [Fact]
    public void Breakdown_LosPorcentajesDeCadaCorteSumanCien_AunConDivisionesNoExactas()
    {
        var thirds = new RevenueBreakdown(
            100m,
            [new("A", 33.33m, 1), new("B", 33.33m, 1), new("C", 33.34m, 1)],
            [new("X", 100m, 3)],
            [new("S", 100m, 3)]);

        var document = Breakdown(thirds);

        foreach (var table in document.Tables)
        {
            var percentColumn = table.Columns.Count - 1;
            table.Rows.Sum(r => (decimal)r[percentColumn]!).Should().BeApproximately(1m, 0.0000001m);
        }
    }

    [Fact]
    public void Breakdown_LosTotalesRepitenElMontoDelPeriodoEnLosTresCortes()
    {
        var document = Breakdown();

        document.Tables[0].Totals.Should().Equal("Total", 100_000m, 5, 1m);
        document.Tables[1].Totals.Should().Equal("Total", 100_000m, 1m);
        document.Tables[2].Totals.Should().Equal("Total", 100_000m, 5, 1m);
    }

    [Fact]
    public void Breakdown_SinPagos_NoDivideEntreCero()
    {
        var empty = new RevenueBreakdown(0m, [], [], []);

        var document = Breakdown(empty);

        document.Tables.Should().OnlyContain(t => t.Rows.Count == 0);
        document.Tables[0].Totals.Should().Equal(new object?[] { "Total", 0m, 0, null });
    }

    [Fact]
    public void Breakdown_TituloSubtituloYArchivo()
    {
        var document = Breakdown();

        document.Title.Should().Be("Recaudación desagregada");
        document.Subtitle.Should().Be("Toda la empresa · 01/11/2026 a 03/11/2026");
        document.FileStem.Should().Be("recaudacion-desagregada-2026-11-01_a_2026-11-03");
        document.Branding.SiteName.Should().BeNull();
    }

    // ── Altas y bajas (A.4) ──────────────────────────────────────────────────

    private static MemberFlow SampleFlow() => new(
        new Dictionary<MemberStatus, int>
        {
            [MemberStatus.Active] = 120, [MemberStatus.Frozen] = 7, [MemberStatus.Overdue] = 11,
        },
        ActiveNow: 120,
        Joins:
        [
            new MemberFlowJoin("Ana García", "30111222", new DateOnly(2026, 11, 1)),
            new MemberFlowJoin("Beto Paz", "30222333", new DateOnly(2026, 11, 2)),
            new MemberFlowJoin("Caro Gil", "30333444", new DateOnly(2026, 11, 3)),
        ],
        Leaves:
        [
            new MemberFlowLeave("Luis Paz", "Trimestral", new DateOnly(2026, 11, 2), "Se mudó"),
            new MemberFlowLeave("Marta Gil", "Mensual Full", new DateOnly(2026, 11, 3), null),
        ],
        Net: 1);

    private static ReportDocument Flow(MemberFlow? data = null) => ReportBuilders.Flow(
        data ?? SampleFlow(), new DateOnly(2026, 11, 1), Today, Context(), NowUtc);

    [Fact]
    public void Flow_ArmaLasTresTablas_ResumenAltasYBajas()
    {
        var document = Flow();

        document.Tables.Select(t => t.Title).Should().Equal("Resumen", "Altas del período", "Bajas del período");
        document.Tables[1].Columns.Select(c => (c.Header, c.Type)).Should().Equal(
            ("Socio", ReportColumnType.Text), ("Documento", ReportColumnType.Text), ("Alta", ReportColumnType.Date));
        document.Tables[2].Columns.Select(c => (c.Header, c.Type)).Should().Equal(
            ("Socio", ReportColumnType.Text), ("Plan", ReportColumnType.Text),
            ("Baja", ReportColumnType.Date), ("Motivo", ReportColumnType.Text));
    }

    [Fact]
    public void Flow_ElResumenTraeLaFotoDeHoyYLosTotalesDelPeriodo()
    {
        var summary = Flow().Tables[0];

        summary.Columns.Select(c => (c.Header, c.Type)).Should().Equal(
            ("Indicador", ReportColumnType.Text), ("Valor", ReportColumnType.Integer));
        summary.Rows.Select(r => ((string)r[0]!, (int)r[1]!)).Should().Equal(
            ("Activos (a hoy)", 120),
            ("Congelados (a hoy)", 7),
            ("En mora (a hoy)", 11),
            ("Suspendidos (a hoy)", 0),
            ("Altas del período", 3),
            ("Bajas del período", 2),
            ("Neto", 1));
    }

    [Fact]
    public void Flow_AltasYBajasLlevanSusFilasCrudas_ConElMotivoNuloSiNoHay()
    {
        var document = Flow();

        document.Tables[1].Rows[0].Should().Equal("Ana García", "30111222", new DateOnly(2026, 11, 1));
        document.Tables[2].Rows[0].Should().Equal(
            "Luis Paz", "Trimestral", new DateOnly(2026, 11, 2), "Se mudó");
        document.Tables[2].Rows[1].Should().Equal(
            new object?[] { "Marta Gil", "Mensual Full", new DateOnly(2026, 11, 3), null });
    }

    [Fact]
    public void Flow_LaNotaDiceQueLosEstadosSonUnaFotoDeHoyYQueLasBajasSonCancelaciones()
    {
        var note = Flow().Note;

        note.Should().Contain("foto de hoy").And.Contain("cancelaciones");
    }

    [Fact]
    public void Flow_TituloSubtituloYArchivo()
    {
        var document = ReportBuilders.Flow(
            SampleFlow(), new DateOnly(2026, 11, 1), Today, Context("Toda la empresa", siteName: null), NowUtc);

        document.Title.Should().Be("Altas y bajas");
        document.Subtitle.Should().Be("Toda la empresa · 01/11/2026 a 03/11/2026");
        document.FileStem.Should().Be("altas-bajas-2026-11-01_a_2026-11-03");
    }

    [Fact]
    public void Flow_SinMovimientos_DaTablasVaciasYElResumenEnCero()
    {
        var empty = new MemberFlow(new Dictionary<MemberStatus, int>(), 0, [], [], 0);

        var document = Flow(empty);

        document.Tables[1].Rows.Should().BeEmpty();
        document.Tables[2].Rows.Should().BeEmpty();
        document.Tables[0].Rows.Should().OnlyContain(r => (int)r[1]! == 0);
    }

    // ── Contrato con el exportador ───────────────────────────────────────────

    /// <summary>Cada celda (y la fila de totales) es del tipo que declara su columna, o null: lo mismo que exige el exportador.</summary>
    private static void AssertWellTyped(ReportDocument document)
    {
        foreach (var table in document.Tables)
        {
            var allRows = table.Totals is null ? table.Rows : table.Rows.Append(table.Totals);
            foreach (var row in allRows)
            {
                row.Should().HaveCount(table.Columns.Count, $"la tabla '{table.Title}' tiene una fila de otro ancho");
                for (var i = 0; i < row.Count; i++)
                    (row[i] is null || Accepts(table.Columns[i].Type, row[i]!)).Should().BeTrue(
                        $"la columna '{table.Columns[i].Header}' de '{table.Title}' ({table.Columns[i].Type}) recibió {row[i]?.GetType().Name}");
            }
        }
    }

    private static bool Accepts(ReportColumnType type, object value) => type switch
    {
        ReportColumnType.Text => value is string,
        ReportColumnType.Money or ReportColumnType.Percent => value is decimal,
        ReportColumnType.Integer => value is int,
        ReportColumnType.Date => value is DateOnly or DateTime,
        _ => false,
    };

    [Fact]
    public void ToDosLosReportes_ConDatos_RespetanElTipoDeCadaColumna_FilasYTotales()
    {
        var from = new DateOnly(2026, 11, 1);
        var cashAt = new DateTime(2026, 11, 2, 12, 0, 0);
        var payments = new PaymentsReport(35_000m, 1,
            [new ReportPaymentRow(Guid.NewGuid(), cashAt, "Ana García", PaymentMethod.Cash, 35_000m)]);
        var debtor = Debtor("Ana García", 40_000m, 35, lastReminderUtc: NowUtc.AddDays(-1));

        AssertWellTyped(ReportBuilders.Payments(payments, from, Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Breakdown(SampleBreakdown(), from, Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Overdue([debtor], Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Flow(SampleFlow(), from, Today, Context(), NowUtc));
    }

    [Fact]
    public void ToDosLosReportes_SinDatos_RespetanElTipoDeCadaColumna_FilasYTotales()
    {
        var from = new DateOnly(2026, 11, 1);

        AssertWellTyped(ReportBuilders.Payments(new PaymentsReport(0m, 0, []), from, Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Breakdown(new RevenueBreakdown(0m, [], [], []), from, Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Overdue([], Today, Context(), NowUtc));
        AssertWellTyped(ReportBuilders.Flow(new MemberFlow(new Dictionary<MemberStatus, int>(), 0, [], [], 0), from, Today, Context(), NowUtc));
    }
}
