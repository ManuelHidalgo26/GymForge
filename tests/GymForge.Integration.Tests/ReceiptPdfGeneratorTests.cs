using System.Text;
using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Services;

namespace GymForge.Integration.Tests;

public class ReceiptPdfGeneratorTests
{
    private static ReceiptDto SampleReceipt(PaymentMethod method = PaymentMethod.Cash) => new(
        Code: "REC-20260611-ABCD1234",
        IssuedAt: new DateTime(2026, 6, 11, 18, 30, 0),
        GymName: "Iron Temple SRL",
        GymTaxId: "30-71234567-8",
        SiteName: "Centro",
        MemberName: "Juan Pérez",
        MemberDocument: "DNI 30111222",
        CashierName: "Ana Admin",
        Method: method,
        CardLast4: method is PaymentMethod.CreditCard ? "4321" : null,
        CardBrand: method is PaymentMethod.CreditCard ? "VISA" : null,
        Items: [new ReceiptItemDto("Cuota mensual Full", 35_000m)],
        OnAccount: 0m,
        Total: 35_000m);

    [Fact]
    public void Generate_ProduceUnPdfValido()
    {
        var bytes = new ReceiptPdfGenerator().Generate(SampleReceipt());

        bytes.Should().NotBeNullOrEmpty();
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Generate_ConTarjeta_TambienGenera()
    {
        var bytes = new ReceiptPdfGenerator().Generate(SampleReceipt(PaymentMethod.CreditCard));

        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}
