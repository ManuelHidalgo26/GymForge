using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Maintenance;

/// <param name="Unfrozen">Membresías que se reactivaron porque terminó su congelamiento.</param>
/// <param name="Expired">Membresías que pasaron a vencidas.</param>
/// <param name="MarkedOverdue">Cargos que entraron en mora ese día (los que ya lo estaban no cuentan).</param>
/// <param name="DunningSent">Avisos de cobro enviados.</param>
/// <param name="ArrearsCleared">Socios que salieron de mora porque ya no tenían cargos vencidos con saldo.</param>
public readonly record struct MaintenanceDayResult(
    int Unfrozen, int Expired, int MarkedOverdue, int DunningSent, int ArrearsCleared);

/// <summary>
/// Mantenimiento de UNA empresa en UN día local: reactiva los congelamientos terminados, vence membresías,
/// marca mora, limpia la mora saldada y dispara dunning, en ese orden. Idempotente: cada paso mira el
/// estado actual, así que repetir un día no duplica. La limpieza de mora corre solo el último día de la
/// ventana de catch-up y antes del dunning: así una falla del sender no la deja sin hacer.
/// </summary>
public sealed class DailyMaintenanceService
{
    private readonly IMembershipRepository _memberships;
    private readonly IChargeRepository _charges;
    private readonly IMemberRepository _members;
    private readonly DunningService _dunning;
    private readonly ICurrentTenant _tenant;

    public DailyMaintenanceService(
        IMembershipRepository memberships, IChargeRepository charges, IMemberRepository members,
        DunningService dunning, ICurrentTenant tenant)
    {
        _memberships = memberships;
        _charges = charges;
        _members = members;
        _dunning = dunning;
        _tenant = tenant;
    }

    /// <param name="isLastDayOfWindow">
    /// True si es el último día de la ventana que se está procesando (el estado de mora que importa es el de
    /// hoy, no el de los días reprocesados). Solo entonces se limpia la mora saldada.
    /// </param>
    public async Task<MaintenanceDayResult> RunForCompanyDayAsync(
        Company company, DateOnly localDay, bool isLastDayOfWindow = true, CancellationToken ct = default)
    {
        // Las lecturas de vencimiento y mora son company-wide (IgnoreQueryFilters + CompanyId explícito);
        // el scope sin sede cierra el filtro por defecto para cualquier otra consulta del flujo.
        using var tenantScope = _tenant.BeginScope(company.Id, Guid.Empty);

        var unfrozen = await UnfreezeEndedFreezesAsync(company.Id, localDay, ct);
        var expired = await ExpireMembershipsAsync(company.Id, localDay, ct);
        var markedOverdue = await MarkOverdueAsync(company.Id, localDay, ct);
        var arrearsCleared = isLastDayOfWindow ? await ClearSettledArrearsAsync(company.Id, localDay, ct) : 0;
        var dunningSent = await _dunning.RunAsync(company.Id, localDay, company.LegalName, ct);

        return new MaintenanceDayResult(unfrozen, expired, markedOverdue, dunningSent, arrearsCleared);
    }

    /// <summary>
    /// Vuelve a Active las membresías cuyo congelamiento llegó a su fin. Va primero: una membresía reactivada
    /// ya trae el vencimiento corrido por el congelamiento, así que no corresponde vencerla ese mismo día.
    /// Reactiva con el día de mantenimiento (no con el reloj): al ser ese día igual o posterior al fin del
    /// congelamiento no hay días sin usar que devolver.
    /// </summary>
    private async Task<int> UnfreezeEndedFreezesAsync(Guid companyId, DateOnly day, CancellationToken ct)
    {
        var ended = await _memberships.GetFreezeEndedAsync(companyId, day, ct);
        if (ended.Count == 0) return 0;

        foreach (var membership in ended)
        {
            membership.Unfreeze(day);
            _memberships.Update(membership);
        }

        await _memberships.SaveChangesAsync(ct);
        return ended.Count;
    }

    /// <summary>No toca el estado del socio: puede tener otra membresía activa.</summary>
    private async Task<int> ExpireMembershipsAsync(Guid companyId, DateOnly day, CancellationToken ct)
    {
        var expired = await _memberships.GetActiveExpiredAsync(companyId, day, ct);
        if (expired.Count == 0) return 0;

        foreach (var membership in expired)
        {
            membership.Expire();
            _memberships.Update(membership);
        }

        await _memberships.SaveChangesAsync(ct);
        return expired.Count;
    }

    private async Task<int> MarkOverdueAsync(Guid companyId, DateOnly day, CancellationToken ct)
    {
        var withBalance = (await _charges.GetOverdueAsync(companyId, day, ct))
            .Where(c => c.AmountOutstanding > 0)
            .ToList();
        if (withBalance.Count == 0) return 0;

        var newlyOverdue = withBalance.Count(EnterArrears);

        await _charges.SaveChangesAsync(ct);
        return newlyOverdue;
    }

    /// <summary>
    /// Pasa el cargo y su socio a mora, tocando solo lo que realmente cambia (no hay que bumpear
    /// UpdatedAt de todo lo moroso cada día). Devuelve true si el cargo entró en mora recién ahora.
    /// </summary>
    private bool EnterArrears(Charge charge)
    {
        var chargeEnters = charge.Status != ChargeStatus.Overdue;
        if (chargeEnters)
            charge.MarkOverdue();

        // Un cargo reembolsado, por ejemplo, no entra en mora ni arrastra al socio.
        if (charge.Status != ChargeStatus.Overdue) return false;

        var memberEnters = charge.Member is { Status: MemberStatus.Active };
        if (memberEnters)
            charge.Member.MarkOverdue();

        if (chargeEnters || memberEnters)
            _charges.Update(charge);

        return chargeEnters;
    }

    /// <summary>
    /// Red de seguridad: el socio en mora que ya no tiene cargos vencidos con saldo vuelve a Active, aunque
    /// el pago que lo saldó no lo haya limpiado. Así el estado se auto-sana. Va después del paso de mora (los
    /// socios que ese paso acaba de marcar tienen un cargo vencido y por eso no se tocan) y antes del dunning.
    /// </summary>
    private async Task<int> ClearSettledArrearsAsync(Guid companyId, DateOnly day, CancellationToken ct)
    {
        var overdueMembers = await _members.GetByStatusAsync(companyId, MemberStatus.Overdue, ct);
        if (overdueMembers.Count == 0) return 0;

        var membersInArrears = (await _charges.GetOverdueAsync(companyId, day, ct))
            .Where(c => c.IsInArrears(day))
            .Select(c => c.MemberId)
            .ToHashSet();

        var settled = overdueMembers.Where(m => !membersInArrears.Contains(m.Id)).ToList();
        if (settled.Count == 0) return 0;

        foreach (var member in settled)
        {
            member.ClearArrears();
            _members.Update(member);
        }

        await _members.SaveChangesAsync(ct);
        return settled.Count;
    }
}
