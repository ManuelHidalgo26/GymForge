using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Application.Tests.Services;

/// <summary>"Hoy" del negocio: la fecha local de la empresa, no la UTC del reloj.</summary>
public class CompanyCalendarTests
{
    private const string ArgentinaTz = "Argentina Standard Time";

    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly FakeClock _clock = new(new DateTime(2026, 7, 22, 15, 0, 0, DateTimeKind.Utc));
    private readonly CapturingLogger<CompanyCalendar> _log = new();

    private CompanyCalendar Sut() => new(_clock, _sites, _log);

    private static Company CompanyIn(string timezone) => Company.Create("Gym Test", "30-12345678-9", timezone);

    private static DateTime Utc(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, DateTimeKind.Utc);

    // ── Fecha local ───────────────────────────────────────────────────────────

    [Fact]
    public void Argentina_At2200_IsStillTheSameLocalDay_WhileUtcAlreadyRolled()
    {
        _clock.Now = Utc(7, 22, 1, 0);   // 22:00 del 21/07 en Argentina

        Sut().TodayFor(CompanyIn(ArgentinaTz)).Should().Be(new DateOnly(2026, 7, 21));
        _clock.Today.Should().Be(new DateOnly(2026, 7, 22), "el reloj sigue siendo UTC");
    }

    [Theory]
    [InlineData(2, 59, 21)]   // 23:59 locales del 21/07: todavía el 21
    [InlineData(3, 0, 22)]    // 00:00 locales del 22/07: ya es el 22
    [InlineData(14, 0, 22)]
    public void ArgentinaDayRollsAtLocalMidnight(int utcHour, int utcMinute, int expectedDay)
    {
        _clock.Now = Utc(7, 22, utcHour, utcMinute);

        Sut().TodayFor(CompanyIn(ArgentinaTz)).Should().Be(new DateOnly(2026, 7, expectedDay));
    }

    [Fact]
    public void EachCompanyUsesItsOwnZone()
    {
        _clock.Now = Utc(7, 22, 2, 0);   // Argentina 21/07 23:00 · Nueva Zelanda 22/07 14:00

        Sut().TodayFor(CompanyIn(ArgentinaTz)).Should().Be(new DateOnly(2026, 7, 21));
        Sut().TodayFor(CompanyIn("New Zealand Standard Time")).Should().Be(new DateOnly(2026, 7, 22));
    }

    // ── Zona inexistente ──────────────────────────────────────────────────────

    [Fact]
    public void UnknownZone_FallsBackToTheClockDate_AndWarns()
    {
        _clock.Now = Utc(7, 22, 1, 0);

        var today = Sut().TodayFor(CompanyIn("Marte/Olympus Mons"));

        today.Should().Be(new DateOnly(2026, 7, 22));
        _log.Levels.Should().Contain(LogLevel.Warning);
    }

    // ── Por id de empresa ─────────────────────────────────────────────────────

    [Fact]
    public async Task TodayAsync_LoadsTheCompany_AndUsesItsZone()
    {
        _clock.Now = Utc(7, 22, 1, 0);
        var company = CompanyIn(ArgentinaTz);
        _sites.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);

        var today = await Sut().TodayAsync(company.Id);

        today.Should().Be(new DateOnly(2026, 7, 21));
    }

    [Fact]
    public async Task TodayAsync_UnknownCompany_Throws()
    {
        _sites.GetCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Company?)null);

        var act = () => Sut().TodayAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
