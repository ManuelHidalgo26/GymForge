using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;

namespace GymForge.Application.Services;

/// <summary>Resuelve al vendedor (el cajero de la venta) para congelar su comisión.</summary>
internal static class SellerLookup
{
    /// <summary>
    /// El vendedor de la empresa, o null si no existe o es de otra empresa: en ese caso la venta sigue,
    /// simplemente sin comisión.
    /// </summary>
    public static async Task<Staff?> FindAsync(
        IStaffRepository staffRepo, Guid companyId, Guid staffId, CancellationToken ct)
    {
        var staff = await staffRepo.GetByIdAsync(staffId, ct);
        return staff is not null && staff.CompanyId == companyId ? staff : null;
    }
}
