using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Dunning;

/// <param name="PhoneUsable">False si el socio no tiene un celular del que se pueda armar un link.</param>
/// <param name="WhatsAppUrl">Link wa.me listo para abrir. Null si no hay nada que enviar (ver <paramref name="Reason"/>).</param>
/// <param name="Reason">Por qué no hay link, para mostrarle al operador.</param>
public record SendWhatsAppReminderResult(bool PhoneUsable, string? WhatsAppUrl, string? Reason);

/// <summary>
/// Recordatorio manual por WhatsApp (link wa.me): arma el mensaje de la etapa que le corresponde a la
/// deuda del socio en esa sede, deja traza y devuelve la URL. Abrirla es cosa de la UI.
/// Funciona siempre, con el cobro automático encendido o apagado.
/// </summary>
public record SendWhatsAppReminderCommand(Guid CompanyId, Guid SiteId, Guid MemberId)
    : IRequest<SendWhatsAppReminderResult>;

public class SendWhatsAppReminderCommandHandler
    : IRequestHandler<SendWhatsAppReminderCommand, SendWhatsAppReminderResult>
{
    private static readonly SendWhatsAppReminderResult NoUsablePhone =
        new(false, null, "El socio no tiene un celular válido.");

    private static readonly SendWhatsAppReminderResult NoDebt =
        new(true, null, "El socio no tiene deuda vencida.");

    private readonly IMemberRepository _members;
    private readonly IChargeRepository _charges;
    private readonly ISiteRepository _sites;
    private readonly ICompanyCalendar _calendar;
    private readonly INotificationLogRepository _log;
    private readonly DunningConfig _config;

    public SendWhatsAppReminderCommandHandler(
        IMemberRepository members, IChargeRepository charges, ISiteRepository sites,
        ICompanyCalendar calendar, INotificationLogRepository log, DunningConfig config)
    {
        _members = members;
        _charges = charges;
        _sites = sites;
        _calendar = calendar;
        _log = log;
        _config = config;
    }

    public async Task<SendWhatsAppReminderResult> Handle(SendWhatsAppReminderCommand cmd, CancellationToken ct)
    {
        var member = await _members.GetByIdAsync(cmd.MemberId, ct);
        if (member is null || member.CompanyId != cmd.CompanyId || member.SiteId != cmd.SiteId)
            throw new KeyNotFoundException("El socio no existe en esta sede.");

        if (member.Mobile is not { } mobile || WhatsAppLink.ToWhatsAppDigits(mobile) is null)
            return NoUsablePhone;

        var company = await _sites.GetCompanyAsync(cmd.CompanyId, ct)
            ?? throw new InvalidOperationException("La empresa no existe.");
        var today = _calendar.TodayFor(company);

        var arrears = await GetMemberArrearsAsync(member, today, ct);
        if (arrears.Count == 0)
            return NoDebt;

        var oldest = arrears.MinBy(c => c.DueDate)!;
        var daysOverdue = today.DayNumber - oldest.DueDate.DayNumber;
        var reachedStage = _config.CurrentStageFor(daysOverdue);

        var message = DunningMessageRenderer.Render((reachedStage ?? _config.FirstStage).Template, new DunningContext(
            member.FirstName, company.LegalName, arrears.Sum(c => c.AmountOutstanding), oldest.DueDate, daysOverdue));

        // Registra que el operador inició el recordatorio; wa.me no confirma que se haya entregado.
        await _log.AppendAsync(
            NotificationLog.Manual(
                cmd.CompanyId, cmd.SiteId, member.Id, oldest.Id,
                NotificationChannel.WhatsApp, reachedStage?.DaysOverdue, mobile),
            ct);
        await _log.SaveChangesAsync(ct);

        return new SendWhatsAppReminderResult(true, WhatsAppLink.Build(mobile, message), null);
    }

    private async Task<List<Charge>> GetMemberArrearsAsync(Member member, DateOnly today, CancellationToken ct) =>
        (await _charges.GetOverdueBySiteAsync(member.CompanyId, member.SiteId, today, ct))
            .Where(c => c.MemberId == member.Id && c.IsInArrears(today))
            .ToList();
}
