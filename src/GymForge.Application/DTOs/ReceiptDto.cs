using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

/// <summary>Ítem del recibo: un cobro saldado (total o parcialmente) por el pago.</summary>
public record ReceiptItemDto(string Description, decimal Amount);

/// <summary>Datos completos de un recibo de pago, listos para renderizar.</summary>
public record ReceiptDto(
    string Code,
    DateTime IssuedAt,
    string GymName,
    string GymTaxId,
    string SiteName,
    string MemberName,
    string MemberDocument,
    string CashierName,
    PaymentMethod Method,
    string? CardLast4,
    string? CardBrand,
    IReadOnlyList<ReceiptItemDto> Items,
    /// <summary>Parte del pago que no se asignó a ningún cobro (queda a cuenta).</summary>
    decimal OnAccount,
    decimal Total);
