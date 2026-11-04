using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

/// <param name="ReceivedAt">Cuándo se cobró, en la hora del gimnasio.</param>
public record ReportPaymentRow(
    Guid PaymentId, DateTime ReceivedAt, string MemberName, PaymentMethod Method, decimal Amount);

public record PaymentsReport(decimal Total, int Count, IReadOnlyList<ReportPaymentRow> Rows);

/// <summary>Recaudación de la sede en un rango de fechas (incluye ambos extremos).</summary>
public record GetPaymentsReportQuery(
    Guid CompanyId, Guid SiteId, DateOnly From, DateOnly To) : IRequest<PaymentsReport>;

public class GetPaymentsReportQueryHandler : IRequestHandler<GetPaymentsReportQuery, PaymentsReport>
{
    private readonly IPaymentRepository _payments;
    private readonly ISiteRepository _sites;

    public GetPaymentsReportQueryHandler(IPaymentRepository payments, ISiteRepository sites)
    {
        _payments = payments;
        _sites = sites;
    }

    public async Task<PaymentsReport> Handle(GetPaymentsReportQuery q, CancellationToken ct)
    {
        var timezone = (await _sites.GetCompanyAsync(q.CompanyId, ct))?.Timezone;
        var (from, to) = ReportPeriod.UtcBounds(q.From, q.To, timezone);

        var list = await _payments.GetByPeriodAsync(q.CompanyId, q.SiteId, from, to, ct);
        var rows = list
            .Select(p => new ReportPaymentRow(
                p.Id, CompanyTime.ToLocal(p.ReceivedAt, timezone), p.Member?.FullName ?? "—", p.Method, p.Amount))
            .ToList();

        return new PaymentsReport(rows.Sum(r => r.Amount), rows.Count, rows);
    }
}
