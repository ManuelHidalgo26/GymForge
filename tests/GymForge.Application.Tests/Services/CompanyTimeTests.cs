using FluentAssertions;
using GymForge.Application.Services;

namespace GymForge.Application.Tests.Services;

public class CompanyTimeTests
{
    private const string Argentina = "Argentina Standard Time";

    [Fact]
    public void StartOfDayUtc_ElDiaDeArgentinaEmpiezaALas0300Utc()
    {
        CompanyTime.StartOfDayUtc(new DateOnly(2026, 11, 3), Argentina)
            .Should().Be(new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void StartOfDayUtc_SinZonaConfigurada_UsaLaDeArgentina(string? timezone)
    {
        CompanyTime.StartOfDayUtc(new DateOnly(2026, 11, 3), timezone)
            .Should().Be(new DateTime(2026, 11, 3, 3, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void StartOfDayUtc_ConZonaInexistente_UsaLaHoraLocalDeLaPc()
    {
        var day = new DateOnly(2026, 11, 3);

        var start = CompanyTime.StartOfDayUtc(day, "Zona\\Que\\No\\Existe");

        start.Should().Be(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local));
    }

    [Fact]
    public void StartOfDayUtc_ElInstanteDeUnPagoDeLas2330LocalesCaeDentroDeSuDia()
    {
        var payment = new DateTime(2026, 11, 4, 2, 30, 0, DateTimeKind.Utc);   // 23:30 del 03/11 en Argentina
        var start = CompanyTime.StartOfDayUtc(new DateOnly(2026, 11, 3), Argentina);
        var nextStart = CompanyTime.StartOfDayUtc(new DateOnly(2026, 11, 4), Argentina);

        payment.Should().BeOnOrAfter(start).And.BeBefore(nextStart);
    }

    // Zonas con cambio de hora a la medianoche: Chile adelanta el reloj (la 00:00 no existe ese día) y Cuba lo atrasa
    // (la 00:00 ocurre dos veces). Un reporte de esos días no puede lanzar ni saltearse una hora.
    [Theory]
    [InlineData("Argentina Standard Time")]
    [InlineData("Pacific SA Standard Time")]
    [InlineData("Cuba Standard Time")]
    public void StartOfDayUtc_ParaTodosLosDiasDelAnio_ElDiaEmpiezaEnSuPrimerInstante(string timezone)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);

        for (var day = new DateOnly(2026, 1, 1); day.Year == 2026; day = day.AddDays(1))
        {
            var start = CompanyTime.StartOfDayUtc(day, timezone);

            TimeZoneInfo.ConvertTimeFromUtc(start, zone).Date.Should().Be(day.ToDateTime(TimeOnly.MinValue),
                $"{day} en {timezone}: el instante de inicio ya es de ese día");
            // Un segundo y no un tick: Windows modela el "24:00" chileno como 23:59:59.999.
            TimeZoneInfo.ConvertTimeFromUtc(start.AddSeconds(-1), zone).Date.Should().Be(day.AddDays(-1).ToDateTime(TimeOnly.MinValue),
                $"{day} en {timezone}: un instante antes todavía es del día anterior");
        }
    }

    // Meta-prueba: el corpus de arriba tiene que ejercitar al menos una medianoche especial (inexistente o ambigua).
    // No se exige que una zona CONCRETA la tenga: el calendario DST de un pais lo define la version de los datos tz
    // del SO (una actualizacion de Windows saco la medianoche ambigua de Cuba 2026) y no es codigo nuestro. Se exige
    // que el corpus en conjunto la conserve; si ninguna zona la tiene mas, hay que reemplazar el corpus.
    [Fact]
    public void StartOfDayUtc_LaPruebaDeTodosLosDiasRealmenteCruzaUnaMedianocheEspecial()
    {
        var special = Corpus
            .SelectMany(timezone =>
            {
                var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
                return Enumerable.Range(0, 365)
                    .Select(offset => new DateOnly(2026, 1, 1).AddDays(offset).ToDateTime(TimeOnly.MinValue))
                    .Where(midnight => zone.IsInvalidTime(midnight) || zone.IsAmbiguousTime(midnight))
                    .Select(midnight => (timezone, midnight));
            })
            .ToList();

        special.Should().NotBeEmpty(
            "alguna zona del corpus debe tener en 2026 una medianoche inexistente o ambigua con los datos tz actuales");
    }

    private static readonly string[] Corpus =
        ["Argentina Standard Time", "Pacific SA Standard Time", "Cuba Standard Time"];
}
