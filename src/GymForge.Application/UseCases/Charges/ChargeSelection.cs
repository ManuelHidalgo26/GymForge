using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;

namespace GymForge.Application.UseCases.Charges;

/// <summary>Selección de los cargos a saldar, compartida por el cobro y el uso de saldo a favor.</summary>
internal static class ChargeSelection
{
    /// <summary>
    /// Cargos explícitos (deben existir y ser del socio y la empresa indicados) o, si no se piden,
    /// todos los pendientes del socio. Las lecturas son secuenciales: comparten DbContext.
    /// </summary>
    public static async Task<List<Charge>> LoadAsync(
        IChargeRepository chargeRepo,
        Guid companyId,
        Guid memberId,
        IReadOnlyList<Guid>? chargeIds,
        CancellationToken ct)
    {
        if (chargeIds is not { Count: > 0 })
            return (await chargeRepo.GetPendingAsync(memberId, ct)).ToList();

        var charges = new List<Charge>(chargeIds.Count);
        foreach (var id in chargeIds)
        {
            var charge = await chargeRepo.GetByIdAsync(id, ct)
                ?? throw new KeyNotFoundException($"Cargo {id} no encontrado.");

            if (charge.MemberId != memberId || charge.CompanyId != companyId)
                throw new InvalidOperationException("El cargo no pertenece al socio indicado.");

            charges.Add(charge);
        }

        return charges;
    }
}
