using ClosedXML.Excel;
using GymForge.Application.UseCases.Reports;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Reporte tabular en Excel (.xlsx) con ClosedXML: una hoja por tabla, con la marca arriba, el encabezado
/// de columnas estilado y celdas numéricas y de fecha reales (el dueño puede re-sumar o armar una dinámica).
/// </summary>
public sealed class ReportXlsxGenerator
{
    private const int MaxSheetNameLength = 31;
    private const string FallbackSheetName = "Reporte";
    private const int HeaderRow = 4;

    private const string MoneyFormat = "\"$\"#,##0.00";
    private const string IntegerFormat = "0";
    private const string PercentFormat = "0.0%";
    private const string DateFormat = "dd/mm/yyyy";
    private const string DateTimeFormat = "dd/mm/yyyy hh:mm";

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#F1F5F9");
    private static readonly XLColor MutedText = XLColor.FromHtml("#64748B");
    private static readonly char[] InvalidSheetChars = ['[', ']', ':', '*', '?', '/', '\\'];

    public byte[] Generate(ReportDocument document)
    {
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brand = XLColor.FromHtml(ReportFormatting.BrandColor(document.Branding.BrandColorHex));

        foreach (var table in document.Tables)
        {
            var sheet = workbook.Worksheets.Add(UniqueSheetName(table.Title, usedNames));
            WriteSheet(sheet, document, table, brand);
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void WriteSheet(IXLWorksheet sheet, ReportDocument document, ReportTable table, XLColor brand)
    {
        WriteBranding(sheet, document, brand);
        WriteHeader(sheet, table);

        var row = HeaderRow + 1;
        foreach (var values in table.Rows)
            WriteRow(sheet, row++, table.Columns, values, bold: false);

        if (table.Totals is { } totals)
            WriteRow(sheet, row++, table.Columns, totals, bold: true);

        sheet.Columns().AdjustToContents(HeaderRow, row - 1);
        sheet.SheetView.FreezeRows(HeaderRow);

        if (!string.IsNullOrWhiteSpace(document.Note))
            sheet.Cell(row + 1, 1).SetValue(document.Note).Style.Font.SetItalic().Font.SetFontColor(MutedText);
    }

    private static void WriteBranding(IXLWorksheet sheet, ReportDocument document, XLColor brand)
    {
        var name = sheet.Cell(1, 1);
        name.SetValue(document.Branding.GymName);
        name.Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(brand);

        sheet.Cell(2, 1).SetValue($"{document.Title} · {document.Subtitle}").Style.Font.SetBold();

        sheet.Cell(3, 1).SetValue($"Generado el {ReportFormatting.DateTimeText(document.GeneratedAt)}")
            .Style.Font.SetFontColor(MutedText);
    }

    private static void WriteHeader(IXLWorksheet sheet, ReportTable table)
    {
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var cell = sheet.Cell(HeaderRow, i + 1);
            cell.SetValue(table.Columns[i].Header);
            cell.Style.Font.SetBold();
            cell.Style.Fill.SetBackgroundColor(HeaderFill);
            cell.Style.Alignment.SetHorizontal(HorizontalAlignment(table.Columns[i].Type));
        }
    }

    private static void WriteRow(
        IXLWorksheet sheet, int rowNumber, IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<object?> values, bool bold)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cell(rowNumber, i + 1);
            WriteValue(cell, columns[i], values[i]);
            if (bold) cell.Style.Font.SetBold();
        }
    }

    /// <summary>Escribe el valor con su tipo real de Excel; el valor ausente deja la celda vacía.</summary>
    private static void WriteValue(IXLCell cell, ReportColumn column, object? value)
    {
        if (value is null) return;

        switch (column.Type)
        {
            case ReportColumnType.Text:
                cell.SetValue(ReportFormatting.Expect<string>(column, value));
                break;
            case ReportColumnType.Money:
                cell.SetValue(ReportFormatting.Expect<decimal>(column, value));
                cell.Style.NumberFormat.SetFormat(MoneyFormat);
                break;
            case ReportColumnType.Integer:
                cell.SetValue(ReportFormatting.Expect<int>(column, value));
                cell.Style.NumberFormat.SetFormat(IntegerFormat);
                break;
            case ReportColumnType.Percent:
                cell.SetValue(ReportFormatting.Expect<decimal>(column, value));
                cell.Style.NumberFormat.SetFormat(PercentFormat);
                break;
            case ReportColumnType.Date:
                WriteDate(cell, column, value);
                break;
            default:
                throw new InvalidOperationException($"Tipo de columna desconocido: {column.Type}.");
        }
    }

    private static void WriteDate(IXLCell cell, ReportColumn column, object value)
    {
        switch (value)
        {
            case DateOnly date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                cell.Style.NumberFormat.SetFormat(DateFormat);
                break;
            case DateTime dateTime:
                cell.SetValue(dateTime);
                cell.Style.NumberFormat.SetFormat(DateTimeFormat);
                break;
            default:
                throw new InvalidOperationException(
                    $"La columna '{column.Header}' ({column.Type}) no admite valores {value.GetType().Name}.");
        }
    }

    private static XLAlignmentHorizontalValues HorizontalAlignment(ReportColumnType type) =>
        type is ReportColumnType.Money or ReportColumnType.Integer or ReportColumnType.Percent
            ? XLAlignmentHorizontalValues.Right
            : XLAlignmentHorizontalValues.Left;

    /// <summary>Nombre de hoja válido para Excel (sin caracteres prohibidos, ≤31) y sin repetir los ya usados.</summary>
    private static string UniqueSheetName(string title, HashSet<string> usedNames)
    {
        var cleaned = string.Concat(title.Where(c => Array.IndexOf(InvalidSheetChars, c) < 0)).Trim().Trim('\'');
        var baseName = cleaned.Length == 0 ? FallbackSheetName : Truncate(cleaned, MaxSheetNameLength);

        var candidate = baseName;
        for (var n = 2; !usedNames.Add(candidate); n++)
        {
            var suffix = $" ({n})";
            candidate = Truncate(baseName, MaxSheetNameLength - suffix.Length) + suffix;
        }

        return candidate;
    }

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
