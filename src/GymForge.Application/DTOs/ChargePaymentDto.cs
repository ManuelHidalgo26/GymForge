using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record ChargeDto(
    Guid Id,
    Guid MemberId,
    ConceptType ConceptType,
    string Description,
    decimal Amount,
    decimal TaxAmount,
    decimal TotalAmount,
    decimal AmountPaid,
    decimal AmountOutstanding,
    DateOnly DueDate,
    ChargeStatus Status,
    string? InvoiceNumber,
    string? FiscalCae)
{
    public static ChargeDto FromEntity(Charge c) => new(
        c.Id, c.MemberId, c.ConceptType, c.Description,
        c.Amount, c.TaxAmount, c.TotalAmount, c.AmountPaid,
        c.AmountOutstanding, c.DueDate, c.Status,
        c.InvoiceNumber, c.FiscalCae);
}

public record PaymentDto(
    Guid Id,
    Guid? MemberId,
    decimal Amount,
    PaymentMethod Method,
    DateTime ReceivedAt,
    PaymentStatus Status,
    string? CardLast4,
    string? CardBrand,
    IReadOnlyList<AllocationDto> Allocations)
{
    public static PaymentDto FromEntity(Payment p) => new(
        p.Id, p.MemberId, p.Amount, p.Method,
        p.ReceivedAt, p.Status, p.CardLast4, p.CardBrand,
        p.Allocations.Select(a => new AllocationDto(a.ChargeId, a.Amount)).ToList());
}

public record AllocationDto(Guid ChargeId, decimal Amount);
