namespace GymForge.Application.Interfaces;

/// <summary>Última fecha LOCAL en que corrió el mantenimiento diario. Persistente entre arranques.</summary>
public interface IMaintenanceCheckpoint
{
    /// <summary>Null si nunca corrió (o si el registro no se puede leer).</summary>
    DateOnly? ReadLastRun();

    void WriteLastRun(DateOnly localDay);
}
