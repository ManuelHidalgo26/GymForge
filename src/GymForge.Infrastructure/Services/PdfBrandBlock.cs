using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GymForge.Infrastructure.Services;

/// <summary>Bloque de marca del gimnasio (logo + nombre + CUIT + sede) que abre el recibo y los reportes.</summary>
internal static class PdfBrandBlock
{
    public static void BrandBlock(
        this IContainer container, string gymName, string? taxId, string? siteName, byte[]? logo) =>
        container.Row(head =>
        {
            if (logo is not null)
                head.ConstantItem(46).Height(46).AlignMiddle().Image(logo).FitArea();

            head.RelativeItem().PaddingLeft(logo is not null ? 10 : 0).Column(col =>
            {
                col.Item().Text(gymName).FontSize(16).Bold();
                if (!string.IsNullOrEmpty(taxId))
                    col.Item().Text($"CUIT {taxId}").FontColor(Colors.Grey.Darken1);
                if (!string.IsNullOrEmpty(siteName))
                    col.Item().Text(siteName).FontColor(Colors.Grey.Darken1);
            });
        });
}
