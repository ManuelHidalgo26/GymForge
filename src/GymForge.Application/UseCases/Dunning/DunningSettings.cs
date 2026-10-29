using System.Text.Json;

namespace GymForge.Application.UseCases.Dunning;

public record DunningStageSettings(int DaysOverdue, string Label, string Template);

/// <summary>
/// Configuración del cobro automático tal como se guarda en <c>Company.DunningJson</c>. Es config de
/// negocio por empresa, así que viaja con la base (a diferencia de las preferencias de la PC).
/// </summary>
public record DunningSettings(bool Enabled, IReadOnlyList<DunningStageSettings> Stages)
{
    /// <summary>Null si no hay nada guardado o el JSON no sirve: el llamador conserva los defaults.</summary>
    public static DunningSettings? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            // Stages puede venir null en un JSON incompleto aunque el tipo no lo admita; sin etapas no hay config útil.
            return JsonSerializer.Deserialize<DunningSettings>(json) is { Stages.Count: > 0 } settings ? settings : null;
        }
        catch (JsonException) { return null; }
    }

    public string ToJson() => JsonSerializer.Serialize(this);

    public static DunningSettings Default() => new(
        false,
        DunningConfig.DefaultStages
            .Select(s => new DunningStageSettings(s.DaysOverdue, s.Label, s.Template))
            .ToList());

    /// <summary>Aplica la configuración al singleton que usa el job (al arrancar y al guardar).</summary>
    public void ApplyTo(DunningConfig config)
    {
        config.Enabled = Enabled;
        config.Stages = Stages.Select(s => new DunningStage(s.DaysOverdue, s.Label, s.Template)).ToList();
    }
}
