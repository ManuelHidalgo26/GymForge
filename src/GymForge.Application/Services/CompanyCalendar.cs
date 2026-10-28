using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GymForge.Application.Services;

/// <summary>
/// El "hoy" del negocio: la fecha local de la empresa según su zona horaria. Toda regla que compare
/// contra una fecha de calendario (vigencia, vencimientos, mora, día de mantenimiento) debe usar esto
/// y no <see cref="IClock.Today"/>, que es UTC: de 21:00 a 24:00 en Argentina la fecha UTC ya es la del
/// día siguiente.
/// </summary>
public interface ICompanyCalendar
{
    /// <summary>Hoy en la zona de la empresa, ya cargada.</summary>
    DateOnly TodayFor(Company company);

    /// <summary>Hoy en la zona de la empresa indicada. Lanza <see cref="KeyNotFoundException"/> si no existe.</summary>
    Task<DateOnly> TodayAsync(Guid companyId, CancellationToken ct = default);
}

public sealed partial class CompanyCalendar : ICompanyCalendar
{
    private readonly IClock _clock;
    private readonly ISiteRepository _sites;
    private readonly ILogger<CompanyCalendar> _log;

    public CompanyCalendar(IClock clock, ISiteRepository sites, ILogger<CompanyCalendar> log)
    {
        _clock = clock;
        _sites = sites;
        _log = log;
    }

    /// <remarks>Si la zona no existe en esta PC se usa la fecha del reloj (UTC) y se avisa en el log.</remarks>
    public DateOnly TodayFor(Company company)
    {
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(company.Timezone);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(_clock.Now, zone));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            LogTimeZoneFallback(ex, company.Id, company.Timezone);
            return _clock.Today;
        }
    }

    public async Task<DateOnly> TodayAsync(Guid companyId, CancellationToken ct = default)
    {
        var company = await _sites.GetCompanyAsync(companyId, ct)
            ?? throw new KeyNotFoundException($"Empresa {companyId} no encontrada.");
        return TodayFor(company);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "La zona horaria '{Timezone}' de la empresa {CompanyId} no existe en esta PC; se usa la fecha UTC del reloj")]
    private partial void LogTimeZoneFallback(Exception ex, Guid companyId, string timezone);
}
