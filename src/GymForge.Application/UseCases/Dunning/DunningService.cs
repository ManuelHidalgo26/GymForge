using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Dunning;

/// <summary>
/// Cobro automático: recorre los cobros vencidos y envía un recordatorio a los socios
/// cuya deuda cumple exactamente una etapa a la fecha dada (p. ej. 7 días de vencido).
/// El disparo por "día exacto" es el gatillo; el <see cref="INotificationLogRepository"/> es el cinturón
/// de seguridad: cada aviso enviado queda registrado por (cargo, etapa) y no se reenvía si el
/// mantenimiento repite un día (catch-up). El envío lo hace un <see cref="INotificationSender"/>
/// (WhatsApp/SMS o log en dev).
/// </summary>
public class DunningService
{
    private readonly IChargeRepository _charges;
    private readonly IMemberRepository _members;
    private readonly INotificationSender _sender;
    private readonly INotificationLogRepository _log;
    private readonly ICurrentTenant _tenant;
    private readonly DunningConfig _config;

    public DunningService(
        IChargeRepository charges, IMemberRepository members, INotificationSender sender,
        INotificationLogRepository log, ICurrentTenant tenant, DunningConfig config)
    {
        _charges = charges;
        _members = members;
        _sender = sender;
        _log = log;
        _tenant = tenant;
        _config = config;
    }

    /// <summary>Corre el dunning de la empresa a la fecha dada. Devuelve cuántos avisos envió.</summary>
    public async Task<int> RunAsync(Guid companyId, DateOnly asOf, string gymName, CancellationToken ct = default)
    {
        if (!_config.Enabled) return 0;

        var overdue = await _charges.GetOverdueAsync(companyId, asOf, ct);
        var sent = 0;

        foreach (var charge in overdue)
        {
            if (charge.AmountOutstanding <= 0) continue;

            var daysOverdue = asOf.DayNumber - charge.DueDate.DayNumber;
            var stage = _config.Stages.FirstOrDefault(s => s.DaysOverdue == daysOverdue);
            if (stage is null) continue;

            if (await _log.WasSentAsync(charge.CompanyId, charge.Id, stage.DaysOverdue, ct)) continue;

            var member = await FindMemberInChargeSiteAsync(charge, ct);
            if (member is null || string.IsNullOrWhiteSpace(member.Mobile)) continue;

            var context = new DunningContext(
                member.FirstName, gymName, charge.AmountOutstanding, charge.DueDate, daysOverdue);
            var body = DunningMessageRenderer.Render(stage.Template, context);
            if (!await _sender.SendAsync(new NotificationMessage(member.Id, member.Mobile, body), ct)) continue;

            await RegisterAsync(charge, stage, member.Mobile, ct);
            sent++;
        }

        return sent;
    }

    /// <summary>La mora se recorre de todas las sedes, pero cada socio vive en la sede de su cargo.</summary>
    private async Task<Member?> FindMemberInChargeSiteAsync(Charge charge, CancellationToken ct)
    {
        using var tenantScope = _tenant.BeginScope(charge.CompanyId, charge.SiteId);
        return await _members.GetByIdAsync(charge.MemberId, ct);
    }

    /// <summary>Deja traza del aviso en la sede del cargo. El canal es el configurado, aunque hoy el sender solo loguee:
    /// por eso la pantalla de morosos no cuenta estos avisos como "último recordatorio".</summary>
    private async Task RegisterAsync(Charge charge, DunningStage stage, string toPhone, CancellationToken ct)
    {
        await _log.AppendAsync(
            NotificationLog.Automatic(
                charge.CompanyId, charge.SiteId, charge.MemberId, charge.Id,
                NotificationChannel.WhatsApp, stage.DaysOverdue, toPhone),
            ct);
        await _log.SaveChangesAsync(ct);
    }
}
