using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.UseCases.Access;

/// <summary>
/// Cómo se resuelve a un socio el código que entrega un lector de códigos (scanner): primero como credencial
/// del gimnasio (<c>Member.TagSerial</c>) y, si no hay match y tiene forma de DNI, como número de documento.
/// Una sola regla compartida por el Gatekeeper (kiosk) y por la búsqueda de la UI (POS).
/// </summary>
public static class CredentialMatch
{
    private const int DniMinDigits = 7;
    private const int DniMaxDigits = 8;

    /// <summary>7-8 dígitos ASCII y nada más (sin espacios, puntos ni letras).</summary>
    public static bool LooksLikeDni(string? code) =>
        code is { Length: >= DniMinDigits and <= DniMaxDigits } && code.All(char.IsAsciiDigit);

    /// <summary>
    /// El socio de la empresa al que corresponde el código, o <c>null</c> si no hay ninguno. El repositorio
    /// filtra por <paramref name="companyId"/> y por el tenant ambiente (sede).
    /// </summary>
    public static async Task<Member?> FindMemberAsync(
        IMemberRepository members, string code, Guid companyId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;

        return await members.FindByTagSerialAsync(code, companyId, ct)
            ?? (LooksLikeDni(code)
                ? await members.FindByDocumentAsync(DocumentType.DNI, code, companyId, ct)
                : null);
    }
}
