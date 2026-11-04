using GymForge.Application.UseCases.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Reporte tabular en PDF (A4 vertical) con QuestPDF: encabezado de marca como el del recibo, y por cada
/// tabla un título de sección, el encabezado, las filas y la fila de totales si viene.
/// </summary>
public sealed class ReportPdfGenerator
{
    static ReportPdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    private const string EmptyTableText = "Sin datos para mostrar.";
    private const float CellPadding = 3f;

    public byte[] Generate(ReportDocument document) =>
        Document.Create(container => container.Page(page =>
        {
            var brand = ReportFormatting.BrandColor(document.Branding.BrandColorHex);

            page.Size(PageSizes.A4);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));

            page.Header().Element(header => ComposeHeader(header, document, brand));
            page.Content().PaddingVertical(12).Element(content => ComposeContent(content, document, brand));
            page.Footer().Element(footer => ComposeFooter(footer, document));
        })).GeneratePdf();

    private static void ComposeHeader(IContainer container, ReportDocument document, string brand) =>
        container.Column(column =>
        {
            var branding = document.Branding;
            var logo = ReportFormatting.TryLoadLogo(branding.LogoPath);

            column.Item().Row(row =>
            {
                row.RelativeItem().BrandBlock(branding.GymName, branding.GymTaxId, branding.SiteName, logo);

                row.ConstantItem(230).Column(title =>
                {
                    title.Item().AlignRight().Text(document.Title).FontSize(14).SemiBold().FontColor(brand);
                    title.Item().AlignRight().Text(document.Subtitle).FontColor(Colors.Grey.Darken1);
                });
            });

            column.Item().PaddingTop(8).LineHorizontal(0.75f).LineColor(brand);
        });

    private static void ComposeContent(IContainer container, ReportDocument document, string brand) =>
        container.Column(column =>
        {
            column.Spacing(16);

            foreach (var table in document.Tables)
                column.Item().Element(section => ComposeSection(section, table, brand));

            if (!string.IsNullOrWhiteSpace(document.Note))
                column.Item().Text(document.Note).FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
        });

    private static void ComposeSection(IContainer container, ReportTable table, string brand) =>
        container.Column(section =>
        {
            section.Spacing(6);
            section.Item().EnsureSpace(80).Text(table.Title).FontSize(12).SemiBold().FontColor(brand);
            section.Item().Element(body => ComposeTable(body, table));
        });

    private static void ComposeTable(IContainer container, ReportTable table) =>
        container.Table(grid =>
        {
            grid.ColumnsDefinition(columns =>
            {
                foreach (var column in table.Columns)
                    columns.RelativeColumn(Weight(column.Type));
            });

            grid.Header(header =>
            {
                foreach (var column in table.Columns)
                    Aligned(header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken4).PaddingBottom(4).PaddingHorizontal(CellPadding), column)
                        .Text(column.Header).SemiBold();
            });

            foreach (var row in table.Rows)
                for (var i = 0; i < table.Columns.Count; i++)
                    Aligned(BodyCell(grid.Cell()), table.Columns[i])
                        .Text(ReportFormatting.Cell(table.Columns[i], row[i]));

            if (table.Rows.Count == 0)
                BodyCell(grid.Cell().ColumnSpan((uint)table.Columns.Count))
                    .Text(EmptyTableText).Italic().FontColor(Colors.Grey.Darken1);

            if (table.Totals is { } totals)
                for (var i = 0; i < table.Columns.Count; i++)
                    Aligned(grid.Cell().BorderTop(1).BorderColor(Colors.Grey.Darken4).PaddingVertical(5).PaddingHorizontal(CellPadding), table.Columns[i])
                        .Text(totals[i] is null ? string.Empty : ReportFormatting.Cell(table.Columns[i], totals[i]))
                        .SemiBold();
        });

    private static IContainer BodyCell(IContainer cell) =>
        cell.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(CellPadding);

    /// <summary>Los números van a la derecha; el resto, a la izquierda.</summary>
    private static IContainer Aligned(IContainer cell, ReportColumn column) =>
        column.Type is ReportColumnType.Money or ReportColumnType.Integer or ReportColumnType.Percent
            ? cell.AlignRight()
            : cell;

    /// <summary>Ancho relativo por tipo de columna: el texto pide más lugar que un número.</summary>
    private static float Weight(ReportColumnType type) => type switch
    {
        ReportColumnType.Text => 3.2f,
        ReportColumnType.Money => 2.1f,
        ReportColumnType.Date => 2.2f,
        ReportColumnType.Integer => 1.4f,
        _ => 1.1f,
    };

    private static void ComposeFooter(IContainer container, ReportDocument document) =>
        container.Row(row =>
        {
            row.RelativeItem().Text($"Generado el {ReportFormatting.DateTimeText(document.GeneratedAt)}")
                .FontSize(8).FontColor(Colors.Grey.Medium);

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Medium));
                text.Span("Página ");
                text.CurrentPageNumber();
                text.Span(" de ");
                text.TotalPages();
            });
        });
}
