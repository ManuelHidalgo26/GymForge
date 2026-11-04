using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;

namespace GymForge.Infrastructure.Services;

/// <summary>El exportador que usa el Desktop: valida el documento y lo despacha al generador del formato pedido.</summary>
public sealed class ReportExporter : IReportExporter
{
    private readonly ReportPdfGenerator _pdf;
    private readonly ReportXlsxGenerator _xlsx;

    public ReportExporter(ReportPdfGenerator pdf, ReportXlsxGenerator xlsx)
    {
        _pdf = pdf;
        _xlsx = xlsx;
    }

    public byte[] Export(ReportDocument document, ReportFormat format)
    {
        EnsureWellFormed(document);

        return format switch
        {
            ReportFormat.Pdf => _pdf.Generate(document),
            ReportFormat.Xlsx => _xlsx.Generate(document),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de reporte desconocido."),
        };
    }

    public string Extension(ReportFormat format) => format switch
    {
        ReportFormat.Pdf => "pdf",
        ReportFormat.Xlsx => "xlsx",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de reporte desconocido."),
    };

    /// <summary>Un reporte mal armado (sin tablas, sin columnas o con filas de otro ancho) falla acá, con su nombre.</summary>
    private static void EnsureWellFormed(ReportDocument document)
    {
        if (document.Tables.Count == 0)
            throw new ArgumentException($"El reporte '{document.Title}' no tiene tablas.", nameof(document));

        foreach (var table in document.Tables)
        {
            if (table.Columns.Count == 0)
                throw new ArgumentException($"La tabla '{table.Title}' no tiene columnas.", nameof(document));

            var badRow = AllRows(table).FirstOrDefault(row => row.Count != table.Columns.Count);
            if (badRow is not null)
                throw new ArgumentException(
                    $"La tabla '{table.Title}' tiene una fila con {badRow.Count} celdas y {table.Columns.Count} columnas.",
                    nameof(document));
        }
    }

    private static IEnumerable<IReadOnlyList<object?>> AllRows(ReportTable table) =>
        table.Totals is null ? table.Rows : table.Rows.Append(table.Totals);
}
