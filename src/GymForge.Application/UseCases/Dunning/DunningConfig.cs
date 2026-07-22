namespace GymForge.Application.UseCases.Dunning;

/// <summary>Una etapa del cobro automático: recordatorio a los N días de vencido el cobro.</summary>
public record DunningStage(int DaysOverdue, string Tone);

/// <summary>
/// Configuración del cobro automático (dunning). Deshabilitado por defecto: el mecanismo
/// queda dormido hasta que se configure un proveedor de mensajería y se active.
/// </summary>
public class DunningConfig
{
    /// <summary>Si es false, el job no envía nada.</summary>
    public bool Enabled { get; set; }

    /// <summary>Recordatorios a 1, 3, 7, 15 y 30 días de vencido, con tono creciente.</summary>
    public IReadOnlyList<DunningStage> Stages { get; set; } =
    [
        new(1, "amable"),
        new(3, "recordatorio"),
        new(7, "firme"),
        new(15, "urgente"),
        new(30, "final"),
    ];
}
