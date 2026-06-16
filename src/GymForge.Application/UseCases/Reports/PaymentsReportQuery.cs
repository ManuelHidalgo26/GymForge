using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

public record ReportPaymentRow(
    Guid PaymentId, DateTime ReceivedAt, string MemberName, PaymentMethod Method, decimal Amount);

public record PaymentsReport(decimal Total, int Count, IReadOnlyList<ReportPaymentRow> Rows);

/// <summary>Recaudación de la sede en un rango de fechas (incluye ambos extremos).</summary>
public record GetPaymentsReportQuery(
    Guid CompanyId, Guid SiteId, DateOnly From, DateOnly To) : IRequest<PaymentsReport>;

public class GetPaymentsReportQueryHandler : IRequestHandler<GetPaymentsReportQuery, PaymentsReport>
{
    private readonly IPaymentRepository _payments;
    public GetPaymentsReportQueryHandler(IPaymentRepository payments) => _payments = payments;

    public async Task<PaymentsReport> Handle(GetPaymentsReportQuery q, CancellationToken ct)
    {
        // ReceivedAt es UTC; el rango local se aproxima con el día calendario completo.
        var from = q.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var to = q.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var list = await _payments.GetByPeriodAsync(q.CompanyId, q.SiteId, from, to, ct);
        var rows = list
            .Select(p => new ReportPaymentRow(p.Id, p.ReceivedAt, p.Member?.FullName ?? "—", p.Method, p.Amount))
            .ToList();

        return new PaymentsReport(rows.Sum(r => r.Amount), rows.Count, rows);
    }
}
