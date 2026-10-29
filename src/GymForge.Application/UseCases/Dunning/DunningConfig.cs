namespace GymForge.Application.UseCases.Dunning;

/// <summary>
/// Una etapa del cobro automático: recordatorio a los N días de vencido el cobro.
/// <paramref name="Template"/> admite los placeholders de <see cref="DunningMessageRenderer"/>.
/// </summary>
public record DunningStage(int DaysOverdue, string Label, string Template);

/// <summary>
/// Configuración del cobro automático (dunning). Singleton por proceso: se hidrata al arrancar desde
/// la empresa y se aplica en caliente al guardar. <see cref="Enabled"/> gobierna solo el job
/// automático; el recordatorio manual por WhatsApp funciona siempre.
/// </summary>
public class DunningConfig
{
    /// <summary>Si es false, el job no envía nada.</summary>
    public bool Enabled { get; set; }

    /// <summary>Recordatorios a 1, 3, 7, 15 y 30 días de vencido, con tono creciente.</summary>
    public IReadOnlyList<DunningStage> Stages { get; set; } = DefaultStages;

    public static readonly IReadOnlyList<DunningStage> DefaultStages =
    [
        new(1, "Aviso amable",
            "¡Hola {nombre}! Te recordamos que tu cuota de {gimnasio} venció el {vencimiento} y quedó un saldo de {monto}. Cuando puedas, acercate a abonarla. ¡Gracias!"),
        new(3, "Recordatorio",
            "Hola {nombre}, seguimos sin registrar el pago de tu cuota de {gimnasio} (venció el {vencimiento}, saldo {monto}). Te esperamos para regularizarla."),
        new(7, "Firme",
            "Hola {nombre}, tu cuota de {gimnasio} está vencida hace {dias} días (saldo {monto}). Para seguir ingresando necesitás ponerte al día."),
        new(15, "Urgente",
            "{nombre}, tu deuda con {gimnasio} lleva {dias} días (saldo {monto}, venció el {vencimiento}). Regularizá tu situación para no perder el acceso."),
        new(30, "Último aviso",
            "{nombre}, último aviso: tu cuota de {gimnasio} está vencida hace {dias} días (saldo {monto}). Comunicate con recepción para no dar de baja tu membresía."),
    ];

    /// <summary>
    /// Etapa vigente de un deudor con N días de atraso: la de mayor <see cref="DunningStage.DaysOverdue"/> que
    /// ya se cumplió. Null si todavía no llegó a la primera etapa.
    /// </summary>
    public DunningStage? CurrentStageFor(int daysOverdue) =>
        Stages.Where(s => s.DaysOverdue <= daysOverdue).MaxBy(s => s.DaysOverdue);

    /// <summary>Etapa de menor antigüedad: texto genérico para quien todavía no llegó a ninguna.</summary>
    public DunningStage FirstStage => Stages.MinBy(s => s.DaysOverdue)
        ?? throw new InvalidOperationException("La configuración de cobro no tiene etapas.");
}
