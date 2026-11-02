using GymForge.Application.Interfaces;

namespace GymForge.Application.UseCases.Members;

/// <summary>
/// Reglas para dar de alta la credencial (<c>Member.TagSerial</c>) de un socio: se guarda sin espacios al borde
/// y es única por empresa (el índice único de la base es el respaldo; esto da el mensaje amable).
/// </summary>
internal static class CredentialEnrollment
{
    /// <summary>Largo máximo de la columna <c>Members.TagSerial</c>.</summary>
    public const int MaxLength = 50;

    /// <exception cref="InvalidOperationException">Otro socio de la empresa ya tiene esa credencial.</exception>
    public static async Task EnsureAvailableAsync(
        IMemberRepository members, Guid companyId, string tagSerial, Guid? exceptMemberId, CancellationToken ct)
    {
        if (await members.IsTagSerialInUseAsync(companyId, tagSerial, exceptMemberId, ct))
            throw new InvalidOperationException("Esa credencial ya está asignada a otro socio.");
    }
}
