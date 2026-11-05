using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

/// <param name="Count">Cantidad de pagos que aportan a la fila.</param>
public record RevenueBreakdownRow(string Label, decimal Amount, int Count)
{
    /// <summary>Participación de la fila en el total del período, como fracción 0..1 (0 si no hubo recaudación).</summary>
    public decimal ShareOf(decimal total) => total > 0 ? Amount / total : 0m;
}

/// <summary>Los tres cortes de la recaudación del período; cada uno suma <paramref name="Total"/>.</summary>
public record RevenueBreakdown(
    decimal Total,
    IReadOnlyList<RevenueBreakdownRow> ByMethod,
    IReadOnlyList<RevenueBreakdownRow> ByConcept,
    IReadOnlyList<RevenueBreakdownRow> BySite);

/// <summary>Recaudación desagregada en un rango de fechas (ambos incluidos). SiteId null = toda la empresa.</summary>
public record GetRevenueBreakdownQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To)
    : IRequest<RevenueBreakdown>;

public class GetRevenueBreakdownQueryHandler : IRequestHandler<GetRevenueBreakdownQuery, RevenueBreakdown>
{
    private const string OnAccountLabel = "A cuenta (saldo a favor)";
    private const string DirectSaleLabel = "Productos (venta directa)";
    private const string InactiveSiteLabel = "Sede inactiva";

    private readonly IPaymentRepository _payments;
    private readonly ISiteRepository _sites;

    public GetRevenueBreakdownQueryHandler(IPaymentRepository payments, ISiteRepository sites)
    {
        _payments = payments;
        _sites = sites;
    }

    public async Task<RevenueBreakdown> Handle(GetRevenueBreakdownQuery query, CancellationToken ct)
    {
        var timezone = (await _sites.GetCompanyAsync(query.CompanyId, ct))?.Timezone;
        var (from, to) = ReportPeriod.UtcBounds(query.From, query.To, timezone);
        var payments = await _payments.GetForBreakdownAsync(query.CompanyId, query.SiteId, from, to, ct);
        var siteNames = (await _sites.GetByCompanyAsync(query.CompanyId, ct)).ToDictionary(s => s.Id, s => s.Name);

        return new RevenueBreakdown(
            payments.Sum(p => p.Amount),
            ByPayment(payments, p => SpanishLabels.For(p.Method)),
            ByConcept(payments),
            ByPayment(payments, p => siteNames.GetValueOrDefault(p.SiteId, InactiveSiteLabel)));
    }

    /// <summary>Agrupa los pagos enteros por una etiqueta (método, sede), de mayor a menor monto.</summary>
    private static IReadOnlyList<RevenueBreakdownRow> ByPayment(
        IEnumerable<Payment> payments, Func<Payment, string> label) =>
        payments
            .GroupBy(label)
            .Select(g => new RevenueBreakdownRow(g.Key, g.Sum(p => p.Amount), g.Count()))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.Label)
            .ToList();

    private static IReadOnlyList<RevenueBreakdownRow> ByConcept(IEnumerable<Payment> payments) =>
        payments
            .SelectMany(ConceptSlices)
            .GroupBy(slice => slice.Label)
            .Select(g => new RevenueBreakdownRow(
                g.Key, g.Sum(s => s.Amount), g.Select(s => s.PaymentId).Distinct().Count()))
            .OrderByDescending(r => r.Amount).ThenBy(r => r.Label)
            .ToList();

    /// <summary>
    /// Reparte un pago entre conceptos: cada asignación a su cargo y lo que ninguna cubre (el excedente) a
    /// "A cuenta", o a "Productos (venta directa)" si el pago nació de una venta del POS (que no genera cargos).
    /// </summary>
    private static IEnumerable<(Guid PaymentId, string Label, decimal Amount)> ConceptSlices(Payment payment)
    {
        foreach (var allocation in payment.Allocations)
            yield return (payment.Id, ConceptLabel(allocation.Charge), allocation.Amount);

        var unallocated = payment.Amount - payment.Allocations.Sum(a => a.Amount);
        if (unallocated > 0)
            yield return (payment.Id, payment.SaleId is null ? OnAccountLabel : DirectSaleLabel, unallocated);
    }

    private static string ConceptLabel(Charge charge) =>
        charge.ConceptType is ConceptType.MembershipFee or ConceptType.SignupFee && charge.Membership is { } membership
            ? membership.MembershipType.Name
            : SpanishLabels.For(charge.ConceptType);
}
