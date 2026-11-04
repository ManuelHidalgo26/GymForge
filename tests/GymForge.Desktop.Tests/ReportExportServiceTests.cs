using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Reports;
using GymForge.Desktop.Services;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>
/// El diálogo nativo de guardado no se puede abrir en un test (sin ventana principal): acá se prueba lo que
/// ocurre antes del diálogo. El guardado y la apertura del archivo los valida qa en Windows.
/// </summary>
public class ReportExportServiceTests
{
    private readonly IReportExporter _exporter = Substitute.For<IReportExporter>();

    private static ReportDocument Document() => new(
        new ReportBranding("Power Gym", null, null, null, null),
        "Morosos", "Sede Central · al 03/11/2026", new DateTime(2026, 11, 3, 12, 0, 0),
        [new ReportTable("Morosos", [new ReportColumn("Socio", ReportColumnType.Text)], [])],
        "morosos-2026-11-03");

    [Theory]
    [InlineData(ReportFormat.Pdf)]
    [InlineData(ReportFormat.Xlsx)]
    public async Task Export_GeneraElArchivoConElFormatoPedido_AntesDelDialogo(ReportFormat format)
    {
        var document = Document();
        _exporter.Export(document, format).Returns([1, 2, 3]);

        var path = await new ReportExportService(_exporter).ExportAsync(document, format);

        _exporter.Received(1).Export(document, format);
        path.Should().BeNull("no hay ventana principal donde mostrar el diálogo, como si el usuario cancelara");
    }

    [Theory]
    [InlineData(@"C:\Users\dueno\reporte.pdf", "pdf", true)]
    [InlineData(@"C:\Users\dueno\REPORTE.PDF", "pdf", true)]
    [InlineData(@"C:\Users\dueno\reporte.xlsx", "xlsx", true)]
    [InlineData(@"C:\Users\dueno\reporte.bat", "pdf", false)]
    [InlineData(@"C:\Users\dueno\reporte.pdf.exe", "pdf", false)]
    [InlineData(@"C:\Users\dueno\reporte", "pdf", false)]
    [InlineData(@"C:\Users\dueno\reporte.xlsx", "pdf", false)]
    public void CanOpen_SoloSiConservaLaExtensionDelFormatoExportado(string path, string extension, bool expected)
    {
        ReportExportService.CanOpen(path, extension).Should().Be(expected);
    }

    [Fact]
    public async Task Export_SiElReporteEstaMalArmado_LanzaSinPedirDondeGuardar()
    {
        _exporter.Export(Arg.Any<ReportDocument>(), Arg.Any<ReportFormat>())
            .Returns(_ => throw new ArgumentException("El reporte no tiene tablas."));

        var act = () => new ReportExportService(_exporter).ExportAsync(Document(), ReportFormat.Pdf);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
