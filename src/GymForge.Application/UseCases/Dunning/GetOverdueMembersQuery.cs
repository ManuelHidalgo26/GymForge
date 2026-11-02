using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using MediatR;

namespace GymForge.Application.UseCases.Dunning;

/// <param name="HasUsableMobile">False si el socio no tiene un celular del que se pueda armar un link de WhatsApp.</param>
/// <param name="CurrentStageLabel">Etapa vigente según la antigüedad de su deuda más vieja (la primera si todavía no llegó a ninguna).</param>
/// <param name="LastReminderAt">Último recordatorio registrado al socio (UTC). Null si nunca se le avisó.</param>
public record OverdueMemberDto(
    Guid MemberId, string FullName, string? Mobile, bool HasUsableMobile,
    decimal TotalOutstanding, DateOnly OldestDueDate, int DaysOverdue, int ChargeCount,
    string CurrentStageLabel, DateTime? LastReminderAt);

/// <summary>Socios con cargos vencidos de UNA sede, del más atrasado al menos (pantalla de morosos).</summary>
public record GetOverdueMembersQuery(Guid CompanyId, Guid SiteId, DateOnly AsOf)
    : IRequest<IReadOnlyList<OverdueMemberDto>>;

public class GetOverdueMembersQueryHandler
    : IRequestHandler<GetOverdueMembersQuery, IReadOnlyList<OverdueMemberDto>>
{
    private readonly IChargeRepository _charges;
    private readonly INotificationLogRepository _log;
    private readonly DunningConfig _config;

    public GetOverdueMembersQueryHandler(
        IChargeRepository charges, INotificationLogRepository log, DunningConfig config)
    {
        _charges = charges;
        _log = log;
        _config = config;
    }

    public async Task<IReadOnlyList<OverdueMemberDto>> Handle(GetOverdueMembersQuery query, CancellationToken ct)
    {
        var arrears = (await _charges.GetOverdueBySiteAsync(query.CompanyId, query.SiteId, query.AsOf, ct))
            .Where(c => c.IsInArrears(query.AsOf))
            .GroupBy(c => c.MemberId);

        // Lecturas secuenciales: comparten DbContext.
        var rows = new List<OverdueMemberDto>();
        foreach (var memberCharges in arrears)
            rows.Add(await ToRowAsync(memberCharges.ToList(), query.AsOf, ct));

        return rows
            .OrderByDescending(r => r.DaysOverdue)
            .ThenByDescending(r => r.TotalOutstanding)
            .ThenBy(r => r.FullName)
            .ToList();
    }

    private async Task<OverdueMemberDto> ToRowAsync(List<Charge> charges, DateOnly asOf, CancellationToken ct)
    {
        var member = charges[0].Member;
        var oldestDue = charges.Min(c => c.DueDate);
        var daysOverdue = asOf.DayNumber - oldestDue.DayNumber;
        var stage = _config.CurrentStageFor(daysOverdue) ?? _config.FirstStage;
        var lastReminder = await _log.GetLastForMemberAsync(member.Id, ct);

        return new OverdueMemberDto(
            member.Id, member.FullName, member.Mobile,
            HasUsableMobile: WhatsAppLink.ToWhatsAppDigits(member.Mobile) is not null,
            charges.Sum(c => c.AmountOutstanding), oldestDue, daysOverdue, charges.Count,
            stage.Label, lastReminder?.SentAt);
    }
}
