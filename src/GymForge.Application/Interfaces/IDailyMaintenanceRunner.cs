namespace GymForge.Application.Interfaces;

/// <summary>Orquesta el catch-up: recorre empresas y días pendientes. Best-effort, idempotente.</summary>
public interface IDailyMaintenanceRunner
{
    Task RunPendingAsync(CancellationToken ct = default);
}
