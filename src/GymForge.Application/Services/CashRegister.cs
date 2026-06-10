using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.Services;

/// <summary>
/// Registra el impacto de un cobro en la caja física. Solo el efectivo afecta el
/// arqueo: tarjeta/transferencia no entran al cajón. No-op si no hay turno abierto.
/// </summary>
public interface ICashRegister
{
    Task PostIfCashAsync(
        PaymentMethod method, Guid? shiftId,
        CashMovementCategory category, decimal amount, Guid? referenceId,
        CancellationToken ct = default);
}

public class CashRegister : ICashRegister
{
    private readonly IShiftRepository _shiftRepo;
    public CashRegister(IShiftRepository shiftRepo) => _shiftRepo = shiftRepo;

    public async Task PostIfCashAsync(
        PaymentMethod method, Guid? shiftId,
        CashMovementCategory category, decimal amount, Guid? referenceId,
        CancellationToken ct = default)
    {
        if (method != PaymentMethod.Cash || shiftId is not { } id)
            return;

        var shift = await _shiftRepo.GetByIdAsync(id, ct);
        if (shift is not { Status: ShiftStatus.Open })
            return;

        shift.Movements.Add(CashMovement.Create(id, CashMovementType.Income, category, amount, referenceId));
        _shiftRepo.Update(shift);
    }
}
