using System.Globalization;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Recibo de pago en PDF (A5) con QuestPDF. Es un comprobante interno,
/// no fiscal: la factura AFIP va por el FiscalBroker.
/// </summary>
public class ReceiptPdfGenerator : IReceiptPdfWriter
{
    static ReceiptPdfGenerator() => QuestPDF.Settings.License = LicenseType.Community;

    // Formato del negocio: $35.000,00 (es-AR con el símbolo pegado al número).
    private static readonly NumberFormatInfo ArsFormat = BuildArsFormat();

    private static NumberFormatInfo BuildArsFormat()
    {
        var nfi = (NumberFormatInfo)CultureInfo.GetCultureInfo("es-AR").NumberFormat.Clone();
        nfi.CurrencySymbol = "$";
        nfi.CurrencyPositivePattern = 0; // $n
        nfi.CurrencyNegativePattern = 1; // -$n
        return nfi;
    }

    private static string Money(decimal value) => value.ToString("C2", ArsFormat);

    // Hex de marca válido (#RRGGBB) o el indigo por defecto para QuestPDF.
    private static string BrandColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex)
        && System.Text.RegularExpressions.Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")
            ? hex.ToUpperInvariant()
            : "#6366F1";

    private static byte[]? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch { return null; }
    }

    private static string MethodLabel(ReceiptDto r)
    {
        var label = r.Method switch
        {
            PaymentMethod.Cash => "Efectivo",
            PaymentMethod.CreditCard => "Tarjeta de crédito",
            PaymentMethod.DebitCard => "Tarjeta de débito",
            PaymentMethod.BankTransfer => "Transferencia",
            PaymentMethod.DirectDebit => "Débito automático",
            PaymentMethod.MercadoPago => "Mercado Pago",
            PaymentMethod.Voucher => "Voucher",
            PaymentMethod.AccountCredit => "Saldo a favor",
            PaymentMethod.Cheque => "Cheque",
            _ => r.Method.ToString(),
        };

        if (!string.IsNullOrEmpty(r.CardLast4))
        {
            label += $" terminada en {r.CardLast4}";
            if (!string.IsNullOrEmpty(r.CardBrand))
                label += $" ({r.CardBrand})";
        }

        return label;
    }

    public byte[] Generate(ReceiptDto r) =>
        Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A5);
            page.Margin(28);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Colors.Grey.Darken4));

            var brand = BrandColor(r.BrandColorHex);
            var logo = TryLoadLogo(r.LogoPath);

            page.Header().Row(row =>
            {
                row.RelativeItem().Row(head =>
                {
                    if (logo is not null)
                        head.ConstantItem(46).Height(46).AlignMiddle().Image(logo).FitArea();

                    head.RelativeItem().PaddingLeft(logo is not null ? 10 : 0).Column(col =>
                    {
                        col.Item().Text(r.GymName).FontSize(16).Bold();
                        if (!string.IsNullOrEmpty(r.GymTaxId))
                            col.Item().Text($"CUIT {r.GymTaxId}").FontColor(Colors.Grey.Darken1);
                        if (!string.IsNullOrEmpty(r.SiteName))
                            col.Item().Text(r.SiteName).FontColor(Colors.Grey.Darken1);
                    });
                });

                row.ConstantItem(160).Column(col =>
                {
                    col.Item().AlignRight().Text("RECIBO").FontSize(14).SemiBold().FontColor(brand);
                    col.Item().AlignRight().Text(r.Code).FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                    col.Item().AlignRight().Text(r.IssuedAt.ToString("dd/MM/yyyy HH:mm")).FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
                });
            });

            page.Content().PaddingVertical(14).Column(col =>
            {
                col.Spacing(10);

                col.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten2);

                col.Item().Column(member =>
                {
                    member.Item().Text(t =>
                    {
                        t.Span("Socio: ").SemiBold();
                        t.Span(r.MemberName);
                    });
                    if (!string.IsNullOrEmpty(r.MemberDocument))
                        member.Item().Text(r.MemberDocument).FontColor(Colors.Grey.Darken1);
                });

                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(110);
                    });

                    table.Header(h =>
                    {
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken4)
                            .PaddingBottom(4).Text("Detalle").SemiBold();
                        h.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken4)
                            .PaddingBottom(4).AlignRight().Text("Importe").SemiBold();
                    });

                    foreach (var item in r.Items)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(5).Text(item.Description);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(5).AlignRight().Text(Money(item.Amount));
                    }

                    if (r.OnAccount > 0)
                    {
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(5).Text("Pago a cuenta");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                            .PaddingVertical(5).AlignRight().Text(Money(r.OnAccount));
                    }
                });

                col.Item().AlignRight().Text(t =>
                {
                    t.Span("TOTAL   ").FontSize(12).SemiBold();
                    t.Span(Money(r.Total)).FontSize(16).Bold().FontColor(brand);
                });

                col.Item().Text(t =>
                {
                    t.Span("Forma de pago: ").SemiBold();
                    t.Span(MethodLabel(r));
                });

                if (!string.IsNullOrEmpty(r.CashierName))
                    col.Item().Text($"Atendido por {r.CashierName}").FontSize(9)
                        .FontColor(Colors.Grey.Darken1);
            });

            page.Footer().AlignCenter().Text("Comprobante no válido como factura")
                .FontSize(8).FontColor(Colors.Grey.Medium);
        })).GeneratePdf();
}
