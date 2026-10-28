using GymForge.Application.Interfaces;
using GymForge.Domain.Enums;

namespace GymForge.Application.Services;

/// <summary>
/// Saca de mora al socio que acaba de saldar su última deuda vencida. El job diario es quien lo
/// marca Overdue; sin esto solo volvía a Active al venderle un plan nuevo.
/// </summary>
public interface IArrearsRelief
{
    /// <summary>
    /// Si el socio está en mora y ya no tiene cargos vencidos con saldo, lo devuelve a Active.
    /// No persiste: el cambio viaja en el SaveChanges del llamador, en la misma transacción que el cobro.
    /// </summary>
    Task ClearIfSettledAsync(Guid companyId, Guid memberId, CancellationToken ct = default);
}

public sealed class ArrearsRelief : IArrearsRelief
{
    private readonly IMemberRepository _members;
    private readonly IChargeRepository _charges;
    private readonly ICompanyCalendar _calendar;

    public ArrearsRelief(IMemberRepository members, IChargeRepository charges, ICompanyCalendar calendar)
    {
        _members = members;
        _charges = charges;
        _calendar = calendar;
    }

    /// <remarks>
    /// Debe llamarse con los cargos recién saldados ya aplicados en memoria y antes de guardar: la lectura de
    /// pendientes devuelve las mismas instancias rastreadas, así que ve el saldo nuevo y no el de la base.
    /// Un socio que no se encuentra en este tenant se ignora: el cobro no puede fallar por el estado, y el
    /// mantenimiento diario lo corrige.
    /// </remarks>
    public async Task ClearIfSettledAsync(Guid companyId, Guid memberId, CancellationToken ct = default)
    {
        var member = await _members.GetByIdAsync(memberId, ct);
        if (member is not { Status: MemberStatus.Overdue }) return;

        var today = await _calendar.TodayAsync(companyId, ct);
        var pending = await _charges.GetPendingAsync(memberId, ct);
        if (pending.Any(charge => charge.IsInArrears(today))) return;

        member.ClearArrears();
    }
}
