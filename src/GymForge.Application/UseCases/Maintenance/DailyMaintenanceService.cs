using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Maintenance;

/// <param name="Expired">Membresías que pasaron a vencidas.</param>
/// <param name="MarkedOverdue">Cargos que entraron en mora ese día (los que ya lo estaban no cuentan).</param>
/// <param name="DunningSent">Avisos de cobro enviados.</param>
/// <param name="ArrearsCleared">Socios que salieron de mora porque ya no tenían cargos vencidos con saldo.</param>
public readonly record struct MaintenanceDayResult(int Expired, int MarkedOverdue, int DunningSent, int ArrearsCleared);

/// <summary>
/// Mantenimiento de UNA empresa en UN día local: vence membresías, marca mora, dispara dunning y limpia
/// la mora saldada, en ese orden. Idempotente: cada paso mira el estado actual, así que repetir un día
/// no duplica.
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

    public async Task<MaintenanceDayResult> RunForCompanyDayAsync(
        Company company, DateOnly localDay, CancellationToken ct = default)
    {
        // Las lecturas de vencimiento y mora son company-wide (IgnoreQueryFilters + CompanyId explícito);
        // el scope sin sede cierra el filtro por defecto para cualquier otra consulta del flujo.
        using var tenantScope = _tenant.BeginScope(company.Id, Guid.Empty);

        var expired = await ExpireMembershipsAsync(company.Id, localDay, ct);
        var markedOverdue = await MarkOverdueAsync(company.Id, localDay, ct);
        var dunningSent = await _dunning.RunAsync(company.Id, localDay, company.LegalName, ct);
        var arrearsCleared = await ClearSettledArrearsAsync(company.Id, localDay, ct);

        return new MaintenanceDayResult(expired, markedOverdue, dunningSent, arrearsCleared);
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
    /// el pago que lo saldó no lo haya limpiado. Así el estado se auto-sana. Va al final: los socios que
    /// el paso de mora acaba de marcar tienen un cargo vencido y por eso no se tocan.
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
