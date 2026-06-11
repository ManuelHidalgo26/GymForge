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
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEf/2mw9QXULo2MHg3qFl6pXwzNXlBx0VmJKUZhMTqiFIksXi2Kcc82gkD7ejYcnx92DZcA+hwcUAlZhJXoaIPMA==";

    private readonly string _publicKeySpki;

    public LicenseService() : this(VendorPublicKey) { }

    /// <summary>Para tests: permite verificar contra otra clave pública.</summary>
    public LicenseService(string publicKeySpki) => _publicKeySpki = publicKeySpki;

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

        if (today <= payload.ExpiresOn)
            return new LicenseState(LicenseStatus.Active, payload.Tier,
                payload.MaxSites, payload.MaxMembers, payload.ExpiresOn, payload.Gym);

        if (today <= payload.ExpiresOn.AddDays(GraceDays))
            return new LicenseState(LicenseStatus.Grace, payload.Tier,
                payload.MaxSites, payload.MaxMembers, payload.ExpiresOn, payload.Gym);

        // Vencida fuera de gracia: se conserva el nombre del tier para el mensaje,
        // pero los límites efectivos vuelven a Free.
        return new LicenseState(LicenseStatus.Expired, payload.Tier,
            LicenseState.FreeMaxSites, LicenseState.FreeMaxMembers, payload.ExpiresOn, payload.Gym);
    }
}
