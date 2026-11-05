using GymForge.Application.UseCases.Reports;

namespace GymForge.Application.Interfaces;

public enum ReportFormat { Pdf, Xlsx }

/// <summary>
/// Vuelca un reporte tabular genérico a un archivo. No sabe de reportes concretos: cada reporte arma su
/// <see cref="ReportDocument"/> y el exportador le da formato (única fuente de formato de moneda y fecha).
/// </summary>
public interface IReportExporter
{
    byte[] Export(ReportDocument document, ReportFormat format);

    /// <summary>Extensión del archivo sin punto: "pdf" o "xlsx".</summary>
    string Extension(ReportFormat format);
}
