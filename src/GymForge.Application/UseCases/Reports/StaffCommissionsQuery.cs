using GymForge.Application.Interfaces;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

/// <summary>Lo que le corresponde a una persona en el período, separado por origen. <c>Total</c> = membresías + productos.</summary>
public record StaffCommissionRow(
    Guid StaffId, string StaffName, decimal MembershipCommission, decimal ProductCommission, decimal Total);

/// <summary>Comisiones del período por persona. Invariante: <c>Total</c> es la suma de los <c>Total</c> de las filas.</summary>
public record StaffCommissions(decimal Total, IReadOnlyList<StaffCommissionRow> Rows)
{
    public decimal MembershipTotal => Rows.Sum(r => r.MembershipCommission);
    public decimal ProductTotal => Rows.Sum(r => r.ProductCommission);
}

/// <summary>
/// Comisiones del personal en un rango de fechas (ambos incluidos). SiteId null = toda la empresa. Suma los
/// montos congelados al vender (Membership.CommissionAmount, SaleLine.CommissionAmount): no recalcula con los
/// porcentajes de hoy.
/// </summary>
public record GetStaffCommissionsQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To)
    : IRequest<StaffCommissions>;

public class GetStaffCommissionsQueryHandler : IRequestHandler<GetStaffCommissionsQuery, StaffCommissions>
{
    private const string UnknownStaffLabel = "Personal no identificado";

    private readonly IMembershipRepository _memberships;
    private readonly ISaleRepository _sales;
    private readonly IStaffRepository _staff;
    private readonly ISiteRepository _sites;

    public GetStaffCommissionsQueryHandler(
        IMembershipRepository memberships, ISaleRepository sales, IStaffRepository staff, ISiteRepository sites)
    {
        _memberships = memberships;
        _sales = sales;
        _staff = staff;
        _sites = sites;
    }

    public async Task<StaffCommissions> Handle(GetStaffCommissionsQuery query, CancellationToken ct)
    {
        var timezone = (await _sites.GetCompanyAsync(query.CompanyId, ct))?.Timezone;
        var (from, to) = ReportPeriod.UtcBounds(query.From, query.To, timezone);

        var memberships = await _memberships.GetSoldBetweenAsync(query.CompanyId, query.SiteId, from, to, ct);
        var lines = await _sales.GetCommissionLinesBetweenAsync(query.CompanyId, query.SiteId, from, to, ct);
        var names = (await _staff.GetAllByCompanyAsync(query.CompanyId, ct)).ToDictionary(s => s.Id, s => s.FullName);

        var byMembership = memberships
            .Where(m => m.SoldByStaffId is not null)
            .GroupBy(m => m.SoldByStaffId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.CommissionAmount));
        var byProduct = lines
            .Where(l => l.CommissionStaffId is not null)
            .GroupBy(l => l.CommissionStaffId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.CommissionAmount));

        var rows = byMembership.Keys.Union(byProduct.Keys)
            .Select(id => Row(id, names, byMembership.GetValueOrDefault(id), byProduct.GetValueOrDefault(id)))
            .OrderByDescending(r => r.Total).ThenBy(r => r.StaffName)
            .ToList();

        return new StaffCommissions(rows.Sum(r => r.Total), rows);
    }

    private static StaffCommissionRow Row(
        Guid staffId, Dictionary<Guid, string> names, decimal membership, decimal product) =>
        new(staffId, names.GetValueOrDefault(staffId, UnknownStaffLabel), membership, product, membership + product);
}
