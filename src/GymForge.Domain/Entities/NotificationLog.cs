using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

/// <summary>
/// Append-only. Never update or delete rows. Registra cada recordatorio de cobranza enviado.
/// No implementa IAuditable a propósito: el log es su propia auditoría. No guarda el cuerpo del
/// mensaje (es reconstruible desde la plantilla y evita duplicar datos personales).
/// </summary>
public class NotificationLog : BaseEntity, ISiteScoped
{
    public Guid MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    /// <summary>Cargo que disparó el aviso. Null = aviso a nivel socio (sin un cargo puntual).</summary>
    public Guid? ChargeId { get; private set; }
    public NotificationChannel Channel { get; private set; }
    public NotificationTrigger Trigger { get; private set; }
    /// <summary>Etapa de dunning (días de vencido) que generó el aviso. Null = manual sin etapa mapeada.</summary>
    public int? StageDaysOverdue { get; private set; }
    /// <summary>Teléfono al que se dirigió (snapshot: el Mobile del socio puede cambiar).</summary>
    public string? ToPhone { get; private set; }
    public DateTime SentAt { get; private set; }

    // Navigation (sin cascade: el log sobrevive al socio)
    public Member? Member { get; private set; }

    private NotificationLog() { }

    /// <summary>Recordatorio iniciado por el operador (link de WhatsApp): no confirma que se haya entregado.</summary>
    public static NotificationLog Manual(
        Guid companyId, Guid siteId, Guid memberId, Guid? chargeId,
        NotificationChannel channel, int? stageDaysOverdue, string? toPhone) => new()
    {
        CompanyId = companyId,
        SiteId = siteId,
        MemberId = memberId,
        ChargeId = chargeId,
        Channel = channel,
        Trigger = NotificationTrigger.Manual,
        StageDaysOverdue = stageDaysOverdue,
        ToPhone = toPhone,
        SentAt = DateTime.UtcNow
    };

    /// <summary>Aviso del job de cobro automático para una etapa de un cargo.</summary>
    public static NotificationLog Automatic(
        Guid companyId, Guid siteId, Guid memberId, Guid chargeId,
        NotificationChannel channel, int stageDaysOverdue, string? toPhone) => new()
    {
        CompanyId = companyId,
        SiteId = siteId,
        MemberId = memberId,
        ChargeId = chargeId,
        Channel = channel,
        Trigger = NotificationTrigger.Automatic,
        StageDaysOverdue = stageDaysOverdue,
        ToPhone = toPhone,
        SentAt = DateTime.UtcNow
    };
}
