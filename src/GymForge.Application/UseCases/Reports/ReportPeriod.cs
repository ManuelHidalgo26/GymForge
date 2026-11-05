using GymForge.Application.Services;

namespace GymForge.Application.UseCases.Reports;

internal static class ReportPeriod
{
    /// <summary>
    /// Rango UTC [desde, hasta) que cubre los días del gimnasio desde <paramref name="from"/> hasta
    /// <paramref name="to"/> (ambos incluidos). Los instantes se guardan en UTC pero un reporte se pide por día
    /// local: un cobro de las 23:30 en Argentina es del día siguiente en UTC y igual pertenece a su día.
    /// </summary>
    public static (DateTime From, DateTime ToExclusive) UtcBounds(DateOnly from, DateOnly to, string? timezoneId) =>
        (CompanyTime.StartOfDayUtc(from, timezoneId), CompanyTime.StartOfDayUtc(to.AddDays(1), timezoneId));
}
