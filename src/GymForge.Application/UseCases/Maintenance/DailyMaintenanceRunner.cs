using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GymForge.Application.UseCases.Maintenance;

/// <summary>
/// Catch-up del mantenimiento diario. La app de escritorio no está siempre prendida, así que al arrancar (y
/// cada tanto mientras está abierta) se procesan los días locales que faltan desde el último checkpoint.
/// Best-effort e idempotente: una falla se loguea y no corta a las demás empresas.
/// </summary>
/// <remarks>
/// El checkpoint es global: avanza hasta el día local más nuevo de las empresas que terminaron bien. Si
/// ninguna terminó, no avanza y la próxima corrida reintenta. Es un límite conocido para multi-empresa
/// (escritorio = 1 empresa): todas comparten la misma ventana.
/// </remarks>
public sealed partial class DailyMaintenanceRunner : IDailyMaintenanceRunner
{
    /// <summary>Tope de días a recuperar: evita un backfill patológico y reenvíos de dunning sin fin.</summary>
    private const int MaxCatchUpDays = 35;

    private readonly ISiteRepository _sites;
    private readonly DailyMaintenanceService _maintenance;
    private readonly IMaintenanceCheckpoint _checkpoint;
    private readonly ICompanyCalendar _calendar;
    private readonly ILogger<DailyMaintenanceRunner> _log;

    public DailyMaintenanceRunner(
        ISiteRepository sites, DailyMaintenanceService maintenance,
        IMaintenanceCheckpoint checkpoint, ICompanyCalendar calendar, ILogger<DailyMaintenanceRunner> log)
    {
        _sites = sites;
        _maintenance = maintenance;
        _checkpoint = checkpoint;
        _calendar = calendar;
        _log = log;
    }

    public async Task RunPendingAsync(CancellationToken ct = default)
    {
        var lastRun = _checkpoint.ReadLastRun();
        DateOnly? newestCompletedDay = null;

        foreach (var company in await _sites.GetCompaniesAsync(ct))
        {
            // El "día" es el local del gimnasio, no el UTC: con la fecha UTC rodaría a las 21:00 en Argentina
            // y el dunning dispararía etapas en el día equivocado.
            var localToday = _calendar.TodayFor(company);
            var firstDay = FirstPendingDay(lastRun, localToday);
            if (firstDay > localToday) continue;

            if (!await TryRunDaysAsync(company, firstDay, localToday, ct)) continue;

            if (newestCompletedDay is null || localToday > newestCompletedDay)
                newestCompletedDay = localToday;
        }

        if (newestCompletedDay is { } completed)
            _checkpoint.WriteLastRun(completed);
    }

    /// <summary>
    /// Primer arranque (sin checkpoint): solo hoy, sin retro-procesar el historial. Si no, el día siguiente
    /// al último procesado, acotado a <see cref="MaxCatchUpDays"/> días hacia atrás.
    /// </summary>
    private static DateOnly FirstPendingDay(DateOnly? lastRun, DateOnly localToday)
    {
        var earliestAllowed = localToday.AddDays(1 - MaxCatchUpDays);
        var next = lastRun?.AddDays(1) ?? localToday;
        return next < earliestAllowed ? earliestAllowed : next;
    }

    /// <summary>Procesa los días en orden; ante una falla corta la ventana de esa empresa y devuelve false.</summary>
    private async Task<bool> TryRunDaysAsync(Company company, DateOnly firstDay, DateOnly lastDay, CancellationToken ct)
    {
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            try
            {
                var result = await _maintenance.RunForCompanyDayAsync(company, day, ct);
                if (result != default)
                    LogDayProcessed(
                        company.Id, day, result.Expired, result.MarkedOverdue, result.DunningSent, result.ArrearsCleared);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogDayFailed(ex, company.Id, day);
                return false;
            }
        }

        return true;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Mantenimiento diario {Day} (empresa {CompanyId}): {Expired} membresías vencidas, {MarkedOverdue} cargos en mora, {DunningSent} avisos, {ArrearsCleared} socios fuera de mora")]
    private partial void LogDayProcessed(
        Guid companyId, DateOnly day, int expired, int markedOverdue, int dunningSent, int arrearsCleared);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "Mantenimiento diario {Day} (empresa {CompanyId}): falló; se reintenta en la próxima corrida")]
    private partial void LogDayFailed(Exception ex, Guid companyId, DateOnly day);
}
