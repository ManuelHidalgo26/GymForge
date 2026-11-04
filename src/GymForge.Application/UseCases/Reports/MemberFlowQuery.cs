using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Reports;

public record MemberFlowJoin(string FullName, string Document, DateOnly JoinDate);

public record MemberFlowLeave(string FullName, string Plan, DateOnly CancelDate, string? Reason);

/// <param name="StatusCounts">Socios por estado a HOY (foto actual; no depende del rango).</param>
/// <param name="ActiveNow">Socios activos a hoy.</param>
/// <param name="Joins">Altas del período: socios cuya fecha de alta cae en el rango.</param>
/// <param name="Leaves">Bajas del período: membresías canceladas en el rango (no incluye las vencidas sin renovar).</param>
/// <param name="Net">Altas menos bajas.</param>
public record MemberFlow(
    IReadOnlyDictionary<MemberStatus, int> StatusCounts,
    int ActiveNow,
    IReadOnlyList<MemberFlowJoin> Joins,
    IReadOnlyList<MemberFlowLeave> Leaves,
    int Net);

/// <summary>Altas y bajas en un rango de fechas (ambos incluidos). SiteId null = toda la empresa.</summary>
public record GetMemberFlowQuery(Guid CompanyId, Guid? SiteId, DateOnly From, DateOnly To) : IRequest<MemberFlow>;

public class GetMemberFlowQueryHandler : IRequestHandler<GetMemberFlowQuery, MemberFlow>
{
    private readonly IMemberRepository _members;
    private readonly IMembershipRepository _memberships;
    private readonly ISiteRepository _sites;

    public GetMemberFlowQueryHandler(
        IMemberRepository members, IMembershipRepository memberships, ISiteRepository sites)
    {
        _members = members;
        _memberships = memberships;
        _sites = sites;
    }

    public async Task<MemberFlow> Handle(GetMemberFlowQuery query, CancellationToken ct)
    {
        // Lecturas secuenciales: comparten DbContext.
        var timezone = (await _sites.GetCompanyAsync(query.CompanyId, ct))?.Timezone;
        var (from, to) = ReportPeriod.UtcBounds(query.From, query.To, timezone);

        var statusCounts = await _members.CountByStatusAsync(query.CompanyId, query.SiteId, ct);
        var joined = await _members.GetJoinedBetweenAsync(query.CompanyId, query.SiteId, query.From, query.To, ct);
        var cancelled = await _memberships.GetCancelledBetweenAsync(query.CompanyId, query.SiteId, from, to, ct);

        var joins = ToJoins(joined).OrderBy(j => j.JoinDate).ThenBy(j => j.FullName).ToList();
        var leaves = ToLeaves(cancelled, timezone).OrderBy(l => l.CancelDate).ThenBy(l => l.FullName).ToList();

        return new MemberFlow(
            statusCounts,
            statusCounts.GetValueOrDefault(MemberStatus.Active),
            joins,
            leaves,
            joins.Count - leaves.Count);
    }

    // El repositorio solo devuelve socios con alta en el rango: si no hay fecha, no hay alta que contar.
    private static IEnumerable<MemberFlowJoin> ToJoins(IEnumerable<Member> members)
    {
        foreach (var member in members)
            if (member.JoinDate is { } joinDate)
                yield return new MemberFlowJoin(member.FullName, member.DocumentNumber, joinDate);
    }

    // La baja se guarda en UTC; en el reporte figura con la fecha del gimnasio.
    private static IEnumerable<MemberFlowLeave> ToLeaves(IEnumerable<Membership> memberships, string? timezone)
    {
        foreach (var membership in memberships)
            if (membership.CancelDate is { } cancelDate)
                yield return new MemberFlowLeave(
                    membership.Member.FullName,
                    membership.MembershipType.Name,
                    DateOnly.FromDateTime(CompanyTime.ToLocal(cancelDate, timezone)),
                    membership.CancelReason);
    }
}
