using GymForge.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace GymForge.Desktop.Services;

/// <summary>
/// Dispara el mantenimiento diario (vence membresías, marca mora, dunning) con catch-up: una corrida al
/// arrancar y otra cada pocas horas mientras la app queda abierta, para cruzar la medianoche sin reiniciar.
/// La app no usa Generic Host, de ahí un <see cref="PeriodicTimer"/> en vez de un BackgroundService.
/// Best-effort: nunca lanza, para no tirar la recepción. Corre como operación de sistema: la auditoría no le
/// atribuye al cajero de turno lo que hace el mantenimiento.
/// </summary>
public sealed class DailyMaintenanceHost(
    IServiceScopeFactory scopeFactory, SessionContext session, SessionCurrentUser currentUser)
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>Arranca el catch-up y el timer en segundo plano; no bloquea el inicio de la UI.</summary>
    public void Start() => _ = Task.Run(RunLoopAsync);

    private async Task RunLoopAsync()
    {
        await RunOnceAsync();

        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync())
            await RunOnceAsync();
    }

    private async Task RunOnceAsync()
    {
        try
        {
            if (session.CompanyId == Guid.Empty) return;   // onboarding pendiente: todavía no hay gimnasio

            using var systemScope = currentUser.BeginSystemScope();
            using var scope = scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IDailyMaintenanceRunner>().RunPendingAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Mantenimiento diario: la corrida best-effort falló");
        }
    }
}
