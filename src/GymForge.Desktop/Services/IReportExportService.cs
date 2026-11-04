using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;

namespace GymForge.Desktop.Services;

/// <summary>Lleva un reporte a un archivo que el usuario elige dónde guardar.</summary>
public interface IReportExportService
{
    /// <summary>
    /// Genera el archivo, deja elegir dónde guardarlo y lo abre. Devuelve la ruta guardada, o <c>null</c> si el
    /// usuario canceló.
    /// </summary>
    /// <exception cref="IOException">No se pudo escribir (p. ej. el archivo está abierto en Excel).</exception>
    Task<string?> ExportAsync(ReportDocument document, ReportFormat format, CancellationToken ct = default);
}
