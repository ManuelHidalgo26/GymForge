using GymForge.Application.UseCases.Reports;

namespace GymForge.Desktop.ViewModels.Reports;

/// <summary>Los reportes del hub de Reportes.</summary>
public enum ReportKind { Payments, RevenueBreakdown, Overdue, MemberFlow }

/// <summary>Una fila de un corte de la recaudación desagregada, con su participación (fracción 0..1) en el total.</summary>
public sealed record BreakdownLine(string Label, decimal Amount, int Count, decimal Share)
{
    public static BreakdownLine From(RevenueBreakdownRow row, decimal total) =>
        new(row.Label, row.Amount, row.Count, row.ShareOf(total));
}
