using GymForge.Application.Interfaces;

namespace GymForge.Integration.Tests;

/// <summary>Reloj controlable: fija "ahora" (UTC) para tests que dependen de la fecha.</summary>
internal sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime Now { get; set; } = utcNow;
    public DateOnly Today => DateOnly.FromDateTime(Now);

    /// <summary>Mediodía en Argentina (UTC-3) del día dado: el día local y el UTC coinciden.</summary>
    public static FakeClock OnDay(DateOnly day) =>
        new(day.ToDateTime(new TimeOnly(15, 0), DateTimeKind.Utc));

    /// <summary>Las 22:00 en Argentina del día dado: en UTC ya es el día siguiente.</summary>
    public static FakeClock At2200Argentina(DateOnly day) =>
        new(day.AddDays(1).ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc));
}
