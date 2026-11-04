using System.Globalization;
using System.Text;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Reports;

/// <summary>
/// Arma el <see cref="ReportDocument"/> de cada reporte a partir de los datos de su consulta. Funciones puras
/// (sin base ni UI): columnas tipadas, totales y valores crudos; el formato lo pone el exportador.
/// </summary>
public static class ReportBuilders
{
    // ── Morosos (A.3) ────────────────────────────────────────────────────────

    private static readonly IReadOnlyList<ReportColumn> OverdueColumns =
    [
        new("Socio", ReportColumnType.Text),
        new("Celular", ReportColumnType.Text),
        new("Deuda", ReportColumnType.Money),
        new("Cargos", ReportColumnType.Integer),
        new("Vence desde", ReportColumnType.Date),
        new("Días de atraso", ReportColumnType.Integer),
        new("Etapa", ReportColumnType.Text),
        new("Último recordatorio", ReportColumnType.Date),
    ];

    /// <param name="debtors">Los morosos de la sede, en el orden que ya trae la consulta (más atrasados primero).</param>
    /// <param name="asOf">El "hoy" del negocio con el que se calculó la mora.</param>
    public static ReportDocument Overdue(
        IReadOnlyList<OverdueMemberDto> debtors, DateOnly asOf, ReportContext context, DateTime generatedAtUtc)
    {
        IReadOnlyList<object?> totals =
            [$"Deudores: {debtors.Count}", null, debtors.Sum(d => d.TotalOutstanding), null, null, null, null, null];

        var table = new ReportTable(
            "Morosos",
            OverdueColumns,
            debtors.Select(d => OverdueRow(d, context)).ToList(),
            totals);

        return NewDocument(
            context, "Morosos", $"{context.Scope} · al {Date(asOf)}", generatedAtUtc, [table], Stem("Morosos", asOf));
    }

    private static IReadOnlyList<object?> OverdueRow(OverdueMemberDto d, ReportContext context) =>
    [
        d.FullName,
        d.Mobile,
        d.TotalOutstanding,
        d.ChargeCount,
        d.OldestDueDate,
        d.DaysOverdue,
        d.CurrentStageLabel,
        d.LastReminderAt is { } sentAt ? DateOnly.FromDateTime(context.ToLocal(sentAt)) : null,
    ];

    // ── Pagos del período (A.1) ──────────────────────────────────────────────

    private static readonly IReadOnlyList<ReportColumn> PaymentColumns =
    [
        new("Fecha", ReportColumnType.Date),
        new("Socio", ReportColumnType.Text),
        new("Método", ReportColumnType.Text),
        new("Monto", ReportColumnType.Money),
    ];

    public static ReportDocument Payments(
        PaymentsReport report, DateOnly from, DateOnly to, ReportContext context, DateTime generatedAtUtc)
    {
        const string title = "Recaudación del período";
        // La etiqueta va bajo "Socio": la columna de fecha no admite texto.
        IReadOnlyList<object?> totals = [null, $"Pagos: {report.Count}", null, report.Total];

        var table = new ReportTable(
            "Pagos del período",
            PaymentColumns,
            report.Rows.Select(PaymentRow).ToList(),
            totals);

        return NewDocument(
            context, title, $"{context.Scope} · {Range(from, to)}", generatedAtUtc, [table], Stem(title, from, to));
    }

    private static IReadOnlyList<object?> PaymentRow(ReportPaymentRow p) =>
        [p.ReceivedAt, p.MemberName, SpanishLabels.For(p.Method), p.Amount];

    // ── Recaudación desagregada (A.2) ────────────────────────────────────────

    public static ReportDocument Breakdown(
        RevenueBreakdown data, DateOnly from, DateOnly to, ReportContext context, DateTime generatedAtUtc)
    {
        const string title = "Recaudación desagregada";
        ReportTable[] tables =
        [
            BreakdownTable("Por método de pago", "Método", data.ByMethod, data.Total, withCount: true),
            BreakdownTable("Por concepto y plan", "Concepto / Plan", data.ByConcept, data.Total, withCount: false),
            BreakdownTable("Por sede", "Sede", data.BySite, data.Total, withCount: true),
        ];

        return NewDocument(
            context, title, $"{context.Scope} · {Range(from, to)}", generatedAtUtc, tables, Stem(title, from, to));
    }

    /// <summary>Un corte de la recaudación: etiqueta, monto, (pagos) y % sobre el total del período.</summary>
    private static ReportTable BreakdownTable(
        string title, string labelHeader, IReadOnlyList<RevenueBreakdownRow> rows, decimal total, bool withCount)
    {
        List<ReportColumn> columns = [new(labelHeader, ReportColumnType.Text), new("Monto", ReportColumnType.Money)];
        if (withCount) columns.Add(new("Pagos", ReportColumnType.Integer));
        columns.Add(new("%", ReportColumnType.Percent));

        var body = rows.Select(r => BreakdownRow(r, total, withCount)).ToList();

        List<object?> totals = ["Total", total];
        if (withCount) totals.Add(rows.Sum(r => r.Count));
        totals.Add(total > 0 ? 1m : null);

        return new ReportTable(title, columns, body, totals);
    }

    private static IReadOnlyList<object?> BreakdownRow(RevenueBreakdownRow row, decimal total, bool withCount)
    {
        List<object?> cells = [row.Label, row.Amount];
        if (withCount) cells.Add(row.Count);
        cells.Add(row.ShareOf(total));
        return cells;
    }

    // ── Altas y bajas (A.4) ──────────────────────────────────────────────────

    public const string FlowNote =
        "Los estados (activos, congelados, en mora, suspendidos) son una foto de hoy, no del cierre del período. " +
        "Las bajas son cancelaciones de membresía registradas en el período: no incluyen membresías vencidas " +
        "sin renovar ni socios dados de baja sin cancelar su membresía.";

    public static ReportDocument Flow(
        MemberFlow flow, DateOnly from, DateOnly to, ReportContext context, DateTime generatedAtUtc)
    {
        ReportTable[] tables = [FlowSummary(flow), FlowJoins(flow), FlowLeaves(flow)];

        return NewDocument(
            context, "Altas y bajas", $"{context.Scope} · {Range(from, to)}", generatedAtUtc, tables,
            Stem("Altas bajas", from, to), FlowNote);
    }

    private static ReportTable FlowSummary(MemberFlow flow)
    {
        int Count(MemberStatus status) => flow.StatusCounts.GetValueOrDefault(status);

        (string Label, int Value)[] indicators =
        [
            ("Activos (a hoy)", flow.ActiveNow),
            ("Congelados (a hoy)", Count(MemberStatus.Frozen)),
            ("En mora (a hoy)", Count(MemberStatus.Overdue)),
            ("Suspendidos (a hoy)", Count(MemberStatus.Suspended)),
            ("Altas del período", flow.Joins.Count),
            ("Bajas del período", flow.Leaves.Count),
            ("Neto", flow.Net),
        ];

        return new ReportTable(
            "Resumen",
            [new("Indicador", ReportColumnType.Text), new("Valor", ReportColumnType.Integer)],
            indicators.Select(i => (IReadOnlyList<object?>)[i.Label, i.Value]).ToList());
    }

    private static ReportTable FlowJoins(MemberFlow flow) => new(
        "Altas del período",
        [
            new("Socio", ReportColumnType.Text),
            new("Documento", ReportColumnType.Text),
            new("Alta", ReportColumnType.Date),
        ],
        flow.Joins.Select(j => (IReadOnlyList<object?>)[j.FullName, j.Document, j.JoinDate]).ToList());

    private static ReportTable FlowLeaves(MemberFlow flow) => new(
        "Bajas del período",
        [
            new("Socio", ReportColumnType.Text),
            new("Plan", ReportColumnType.Text),
            new("Baja", ReportColumnType.Date),
            new("Motivo", ReportColumnType.Text),
        ],
        flow.Leaves.Select(l => (IReadOnlyList<object?>)[l.FullName, l.Plan, l.CancelDate, l.Reason]).ToList());

    // ── Piezas comunes ───────────────────────────────────────────────────────

    private static ReportDocument NewDocument(
        ReportContext context, string title, string subtitle, DateTime generatedAtUtc,
        IReadOnlyList<ReportTable> tables, string fileStem, string? note = null) =>
        new(context.Branding, title, subtitle, context.ToLocal(generatedAtUtc), tables, fileStem, note);

    private static string Range(DateOnly from, DateOnly to) =>
        from == to ? Date(from) : $"{Date(from)} a {Date(to)}";

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Nombre de archivo sugerido: título en minúsculas, sin acentos ni espacios, más la fecha o el rango.</summary>
    private static string Stem(string title, DateOnly from, DateOnly? to = null) =>
        to is { } end && end != from
            ? $"{Slug(title)}-{Iso(from)}_a_{Iso(end)}"
            : $"{Slug(title)}-{Iso(from)}";

    private static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(char.ToLowerInvariant(c));
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }

        return slug.ToString().TrimEnd('-');
    }
}
