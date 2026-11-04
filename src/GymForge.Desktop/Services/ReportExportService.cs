using System.Diagnostics;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>
/// Exporta un reporte con el diálogo nativo de guardado: es un archivo que el dueño se lleva (lo imprime, lo
/// manda al contador), a diferencia del recibo, que el sistema archiva solo (<see cref="ReceiptService"/>).
/// </summary>
public sealed class ReportExportService : IReportExportService
{
    private readonly IReportExporter _exporter;

    public ReportExportService(IReportExporter exporter) => _exporter = exporter;

    public async Task<string?> ExportAsync(
        ReportDocument document, ReportFormat format, CancellationToken ct = default)
    {
        // Primero se genera (fuera del hilo de UI: un reporte grande tarda): un reporte mal armado falla antes de
        // pedirle al usuario dónde guardarlo.
        var bytes = await Task.Run(() => _exporter.Export(document, format), ct);

        if (MainStorage() is not { } storage) return null;

        var extension = _exporter.Extension(format);
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar reporte",
            SuggestedFileName = $"{document.FileStem}.{extension}",
            DefaultExtension = extension,
            FileTypeChoices = [FileType(format)],
        });

        if (file?.TryGetLocalPath() is not { } destination) return null;

        await File.WriteAllBytesAsync(destination, bytes, ct);
        if (CanOpen(destination, extension)) TryOpen(destination);
        return destination;
    }

    private static FilePickerFileType FileType(ReportFormat format) => format switch
    {
        ReportFormat.Pdf => new FilePickerFileType("Documento PDF (*.pdf)") { Patterns = ["*.pdf"] },
        ReportFormat.Xlsx => new FilePickerFileType("Libro de Excel (*.xlsx)") { Patterns = ["*.xlsx"] },
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Formato de reporte desconocido."),
    };

    /// <summary>
    /// El usuario puede escribir cualquier nombre en el diálogo de guardado: solo se abre el archivo si conserva la
    /// extensión del formato exportado, así un nombre como <c>reporte.bat</c> no se ejecuta con el shell.
    /// </summary>
    public static bool CanOpen(string path, string expectedExtension) =>
        string.Equals(Path.GetExtension(path), "." + expectedExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>Abre el archivo con el programa del sistema. Best-effort: si no hay visor, el archivo igual quedó guardado.</summary>
    private static void TryOpen(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo abrir el reporte {Path}", path);
        }
    }

    private static IStorageProvider? MainStorage() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.StorageProvider;
}
