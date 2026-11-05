namespace GymForge.Application.Services;

/// <summary>
/// Pasa entre los instantes UTC (como se guardan los pagos, avisos y cancelaciones) y la hora del gimnasio.
/// Para el "hoy" del negocio usar <see cref="ICompanyCalendar"/>.
/// </summary>
public static class CompanyTime
{
    private const string DefaultTimezone = "Argentina Standard Time";

    /// <remarks>Si la zona no existe en esta PC se usa la hora local de la PC.</remarks>
    public static DateTime ToLocal(DateTime utc, string? timezoneId) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), ZoneOf(timezoneId));

    /// <summary>
    /// El instante UTC en que empieza el día local <paramref name="day"/> del gimnasio. Respeta los cambios de hora
    /// que caen en la medianoche: si la medianoche no existe (el reloj adelanta) el día arranca en el salto, y si
    /// ocurre dos veces (el reloj atrasa) arranca en la primera.
    /// </summary>
    /// <remarks>Si la zona no existe en esta PC se usa la hora local de la PC.</remarks>
    public static DateTime StartOfDayUtc(DateOnly day, string? timezoneId)
    {
        var zone = ZoneOf(timezoneId);
        var midnight = day.ToDateTime(TimeOnly.MinValue);

        // En una hora inexistente GetUtcOffset devuelve el offset estándar, que ubica la medianoche justo en el salto.
        var offset = zone.IsAmbiguousTime(midnight)
            ? zone.GetAmbiguousTimeOffsets(midnight).Max()
            : zone.GetUtcOffset(midnight);

        return DateTime.SpecifyKind(midnight - offset, DateTimeKind.Utc);
    }

    private static TimeZoneInfo ZoneOf(string? timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(
                string.IsNullOrWhiteSpace(timezoneId) ? DefaultTimezone : timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Local;
        }
    }
}
