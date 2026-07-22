using GymForge.Application.Interfaces;

namespace GymForge.Application.UseCases.Dunning;

/// <summary>
/// Cobro automático: recorre los cobros vencidos y envía un recordatorio a los socios
/// cuya deuda cumple exactamente una etapa a la fecha dada (p. ej. 7 días de vencido).
/// Al enviar solo en el día exacto de cada etapa, es idempotente si corre una vez por día.
/// El envío lo hace un <see cref="INotificationSender"/> (WhatsApp/SMS o log en dev).
/// </summary>
public class DunningService
{
    private readonly IChargeRepository _charges;
    private readonly IMemberRepository _members;
    private readonly INotificationSender _sender;
    private readonly DunningConfig _config;

    public DunningService(
        IChargeRepository charges, IMemberRepository members,
        INotificationSender sender, DunningConfig config)
    {
        _charges = charges;
        _members = members;
        _sender = sender;
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

            var member = await _members.GetByIdAsync(charge.MemberId, ct);
            if (member is null || string.IsNullOrWhiteSpace(member.Mobile)) continue;

            var body = DunningTemplates.Build(stage, member.FirstName, charge.AmountOutstanding, charge.DueDate, gymName);
            if (await _sender.SendAsync(new NotificationMessage(member.Mobile!, body), ct))
                sent++;
        }

        return sent;
    }
}
