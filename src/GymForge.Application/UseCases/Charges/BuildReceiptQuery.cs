using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Charges;

/// <summary>
/// Arma los datos del recibo de un pago ya registrado. Usado al cobrar
/// (recibo PDF inmediato) y reutilizable para reimprimir desde el historial.
/// </summary>
public record BuildReceiptQuery(Guid PaymentId, Guid CompanyId) : IRequest<ReceiptDto>;

public class BuildReceiptQueryHandler : IRequestHandler<BuildReceiptQuery, ReceiptDto>
{
    private readonly IPaymentRepository _payments;
    private readonly IChargeRepository _charges;
    private readonly IMemberRepository _members;
    private readonly ISiteRepository _sites;
    private readonly IStaffRepository _staff;
    private readonly ISaleRepository _sales;

    public BuildReceiptQueryHandler(
        IPaymentRepository payments,
        IChargeRepository charges,
        IMemberRepository members,
        ISiteRepository sites,
        IStaffRepository staff,
        ISaleRepository sales)
    {
        _payments = payments;
        _charges = charges;
        _members = members;
        _sites = sites;
        _staff = staff;
        _sales = sales;
    }

    public async Task<ReceiptDto> Handle(BuildReceiptQuery query, CancellationToken ct)
    {
        var payment = await _payments.GetByIdAsync(query.PaymentId, ct);
        if (payment is null || payment.CompanyId != query.CompanyId)
            throw new InvalidOperationException("Pago no encontrado.");

        var company = await _sites.GetCompanyAsync(payment.CompanyId, ct);
        var site = await _sites.GetSiteAsync(payment.SiteId, ct);
        var member = payment.MemberId is { } memberId
            ? await _members.GetByIdAsync(memberId, ct)
            : null;
        var cashier = await _staff.GetByIdAsync(payment.CashierId, ct);

        var items = new List<ReceiptItemDto>();
        foreach (var allocation in payment.Allocations)
        {
            var charge = await _charges.GetByIdAsync(allocation.ChargeId, ct);
            items.Add(new ReceiptItemDto(charge?.Description ?? "Cobro", allocation.Amount));
        }

        // Venta de producto: el detalle sale de las líneas de la venta. Se factura
        // con IVA incluido por línea para que la suma cuadre con el total del pago.
        if (items.Count == 0 && payment.SaleId is { } saleId)
        {
            var sale = await _sales.GetByIdAsync(saleId, ct);
            if (sale is not null)
                items.AddRange(sale.Lines.Select(l => new ReceiptItemDto(
                    LineDescription(l), Math.Round(l.LineTotal * (1 + l.TaxRate), 2))));
        }

        var onAccount = payment.Amount - items.Sum(i => i.Amount);
        var issuedAt = ToCompanyLocal(payment.ReceivedAt, company?.Timezone);

        return new ReceiptDto(
            Code: $"REC-{issuedAt:yyyyMMdd}-{payment.Id.ToString("N")[..8].ToUpperInvariant()}",
            IssuedAt: issuedAt,
            GymName: company?.LegalName ?? "GymForge",
            GymTaxId: company?.TaxId ?? string.Empty,
            SiteName: site?.Name ?? string.Empty,
            MemberName: member is not null
                ? $"{member.FirstName} {member.LastName}"
                : payment.MemberId is null ? "Consumidor Final" : "Socio",
            MemberDocument: member is null
                ? string.Empty
                : $"{DocumentLabel(member.DocumentType)} {member.DocumentNumber}",
            CashierName: cashier is null ? string.Empty : $"{cashier.FirstName} {cashier.LastName}",
            Method: payment.Method,
            CardLast4: payment.CardLast4,
            CardBrand: payment.CardBrand,
            Items: items,
            OnAccount: onAccount,
            Total: payment.Amount);
    }

    /// <summary>Descripción de la línea con la cantidad cuando es mayor a 1 (ej. "Agua 500ml x2").</summary>
    private static string LineDescription(Domain.Entities.SaleLine line) =>
        line.Quantity > 1 ? $"{line.Description} x{line.Quantity:0.##}" : line.Description;

    private static string DocumentLabel(DocumentType type) => type switch
    {
        DocumentType.PASS => "Pasaporte",
        DocumentType.CE => "Cédula",
        _ => type.ToString(),
    };

    /// <summary>ReceivedAt se guarda en UTC; el recibo se emite en la hora del gimnasio.</summary>
    private static DateTime ToCompanyLocal(DateTime utc, string? timezone)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timezone) ? "Argentina Standard Time" : timezone);
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), tz);
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.ToLocalTime();
        }
    }
}
