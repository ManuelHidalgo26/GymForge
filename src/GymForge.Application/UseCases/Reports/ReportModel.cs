namespace GymForge.Application.UseCases.Reports;

/// <summary>Tipo de una columna: gobierna cómo la formatea el exportador (PDF) y qué celda escribe (Excel).</summary>
public enum ReportColumnType { Text, Money, Integer, Date, Percent }

public record ReportColumn(string Header, ReportColumnType Type);

/// <summary>
/// Una tabla del reporte. <paramref name="Rows"/> son filas de celdas crudas, una por columna y en el mismo orden
/// que <paramref name="Columns"/>: Text = string, Money = decimal, Integer = int, Date = DateOnly o DateTime,
/// Percent = decimal como fracción 0..1; null = sin dato. <paramref name="Totals"/> es una fila de cierre
/// opcional con la misma aridad.
/// </summary>
public record ReportTable(
    string Title,
    IReadOnlyList<ReportColumn> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    IReadOnlyList<object?>? Totals = null);

/// <summary>Marca del gimnasio para el encabezado (mismos campos que usa el recibo).</summary>
public record ReportBranding(
    string GymName, string? GymTaxId, string? SiteName, string? LogoPath, string? BrandColorHex);

/// <param name="Title">"Morosos", "Recaudación desagregada", ...</param>
/// <param name="Subtitle">"Centro · 01/11/2026 a 03/11/2026"</param>
/// <param name="GeneratedAt">Cuándo se generó, en la hora del gimnasio.</param>
/// <param name="FileStem">Nombre sugerido del archivo, sin extensión: "morosos-2026-11-03".</param>
/// <param name="Note">Aclaración al pie sobre el alcance de los números (opcional).</param>
public record ReportDocument(
    ReportBranding Branding,
    string Title,
    string Subtitle,
    DateTime GeneratedAt,
    IReadOnlyList<ReportTable> Tables,
    string FileStem,
    string? Note = null);
