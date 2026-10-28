using System.Security.Cryptography;
using GymForge.Application.Interfaces;

namespace GymForge.Application.UseCases.Licensing;

public class LicenseService : ILicenseService
{
    /// <summary>Días de operación tras el vencimiento antes de degradar a Free (gracia offline).</summary>
    public const int GraceDays = 15;

    // Clave pública del vendedor (SPKI base64). La privada NO está en el repo:
    // vive en %LOCALAPPDATA%\GymForge\vendor\ y se administra con tools/GymForge.LicenseGen.
    public const string VendorPublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAElYRJMZQxlo01bM2zMoNVIct7Cp5c7piwwjJ5MJ8DZDrO5jXuBszP6f/wlNdaCkNZ1nW0ZgdWZ4v43r7oAnGGFQ==";

    private readonly string _publicKeySpki;
    private readonly IMachineFingerprint? _fingerprint;

    /// <param name="fingerprint">Huella de esta PC. Null (herramientas, tests) = no se chequea el atado a máquina.</param>
    public LicenseService(IMachineFingerprint? fingerprint = null) : this(VendorPublicKey, fingerprint) { }

    /// <summary>Para tests: permite verificar contra otra clave pública.</summary>
    public LicenseService(string publicKeySpki, IMachineFingerprint? fingerprint = null)
    {
        _publicKeySpki = publicKeySpki;
        _fingerprint = fingerprint;
    }

    public LicenseState Resolve(string? licenseKey, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(licenseKey)) return LicenseState.Free;

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_publicKeySpki), out _);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return LicenseState.Free;
        }

        var payload = LicenseCodec.TryDecode(licenseKey, ecdsa);
        if (payload is null) return LicenseState.Free;

        var status = StatusOn(payload.ExpiresOn, today);

        // Vencida fuera de gracia: se conserva el nombre del tier para el mensaje,
        // pero los límites efectivos vuelven a Free.
        var keepsPaidLimits = status != LicenseStatus.Expired;
        return new LicenseState(
            status, payload.Tier,
            keepsPaidLimits ? payload.MaxSites : LicenseState.FreeMaxSites,
            keepsPaidLimits ? payload.MaxMembers : LicenseState.FreeMaxMembers,
            payload.ExpiresOn, payload.Gym, payload.Cuit, IsBoundToAnotherMachine(payload));
    }

    private static LicenseStatus StatusOn(DateOnly expiresOn, DateOnly today)
    {
        if (today <= expiresOn) return LicenseStatus.Active;
        return today <= expiresOn.AddDays(GraceDays) ? LicenseStatus.Grace : LicenseStatus.Expired;
    }

    /// <summary>
    /// La clave trae el código de una PC y esta no es esa. Solo se avisa: un cambio de hardware
    /// no debe dejar al gimnasio sin sistema. Sin código en la clave, o sin poder calcular el de
    /// esta PC, no hay con qué comparar.
    /// </summary>
    private bool IsBoundToAnotherMachine(LicensePayload payload)
    {
        if (string.IsNullOrEmpty(payload.MachineId)) return false;

        var current = _fingerprint?.Current();
        return !string.IsNullOrEmpty(current)
            && !string.Equals(payload.MachineId, current, StringComparison.OrdinalIgnoreCase);
    }
}
