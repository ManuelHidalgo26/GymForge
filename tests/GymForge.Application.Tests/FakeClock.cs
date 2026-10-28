using GymForge.Application.Interfaces;

namespace GymForge.Application.Tests;

/// <summary>
/// Reloj controlable: fija "ahora" (UTC). Igual que el real, su <see cref="Today"/> es la fecha UTC,
/// que de 21:00 a 24:00 en Argentina ya es la del día siguiente.
/// </summary>
internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime Now { get; set; } = utcNow;
    public DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Mediodía en Argentina (UTC-3) del día dado: el día local y el UTC coinciden.</summary>
    public static DateTime ArgentinaNoon(DateOnly day) =>
        day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc);

    /// <summary>Las 22:00 en Argentina del día dado: en UTC ya es el día siguiente.</summary>
    public static DateTime Argentina2200(DateOnly day) =>
        day.AddDays(1).ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
}
