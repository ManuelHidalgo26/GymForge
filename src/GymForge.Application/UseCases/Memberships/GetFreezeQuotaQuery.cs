using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using MediatR;

namespace GymForge.Application.UseCases.Memberships;

/// <summary>Cupo anual de congelamiento de una membresía, en el año calendario local de la empresa.</summary>
/// <param name="Today">Hoy en la zona de la empresa: desde acá arranca un congelamiento nuevo.</param>
/// <param name="Year">Año calendario al que corresponde el cupo.</param>
/// <param name="MaxDays">Tope anual de la empresa.</param>
/// <param name="UsedDays">Días de congelamiento ya usados en <paramref name="Year"/>.</param>
public readonly record struct FreezeQuotaDto(DateOnly Today, int Year, int MaxDays, int UsedDays)
{
    public int RemainingDays => Math.Max(0, MaxDays - UsedDays);
}

/// <summary>Cupo de congelamiento que le queda a la membresía, para mostrarlo antes de congelar.</summary>
public record GetFreezeQuotaQuery(Guid MembershipId) : IRequest<FreezeQuotaDto>;

public class GetFreezeQuotaQueryHandler : IRequestHandler<GetFreezeQuotaQuery, FreezeQuotaDto>
{
    private readonly IMembershipRepository _memberships;
    private readonly ISiteRepository _sites;
    private readonly ICompanyCalendar _calendar;

    public GetFreezeQuotaQueryHandler(
        IMembershipRepository memberships, ISiteRepository sites, ICompanyCalendar calendar)
    {
        _memberships = memberships;
        _sites = sites;
        _calendar = calendar;
    }

    public async Task<FreezeQuotaDto> Handle(GetFreezeQuotaQuery query, CancellationToken ct)
    {
        var membership = await _memberships.GetByIdAsync(query.MembershipId, ct)
            ?? throw new KeyNotFoundException($"Membresía {query.MembershipId} no encontrada.");
        var company = await _sites.GetCompanyAsync(membership.CompanyId, ct)
            ?? throw new KeyNotFoundException($"Empresa {membership.CompanyId} no encontrada.");

        // El año del cupo es el del día local: con la fecha UTC, el 31/12 de 21:00 a 24:00 se vería el cupo del año siguiente.
        var today = _calendar.TodayFor(company);
        return new FreezeQuotaDto(
            today, today.Year, company.MaxFreezeDaysPerYear, membership.FreezeDaysUsedIn(today.Year));
    }
}
