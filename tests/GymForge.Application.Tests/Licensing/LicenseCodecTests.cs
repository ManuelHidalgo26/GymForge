using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GymForge.Application.UseCases.Licensing;

namespace GymForge.Application.Tests.Licensing;

public class LicenseCodecTests : IDisposable
{
    private static readonly DateOnly Issued = new(2026, 6, 1);
    private static readonly DateOnly Expires = new(2027, 6, 1);

    private readonly ECDsa _keys = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public void Dispose()
    {
        _keys.Dispose();
        GC.SuppressFinalize(this);
    }

    private static LicensePayload Payload(string? machineId = null) => new(
        Guid.Parse("11111111-2222-3333-4444-555555555555"), "Iron Temple SRL", "30-71234567-1",
        "Pro", 3, 1000, Issued, Expires, machineId);

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Arma una clave a mano con el JSON dado, firmándolo con la clave de test (como hacía el emisor viejo).</summary>
    private string SignedKeyFor(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var signature = _keys.SignData(bytes, HashAlgorithmName.SHA256);
        return $"GYMF.{Base64Url(bytes)}.{Base64Url(signature)}";
    }

    [Fact]
    public void RoundTrip_ConMachineId_SePreserva()
    {
        var key = LicenseCodec.Encode(Payload("A1B2-C3D4-E5F6-0718"), _keys);

        var decoded = LicenseCodec.TryDecode(key, _keys);

        decoded.Should().Be(Payload("A1B2-C3D4-E5F6-0718"));
        decoded!.MachineId.Should().Be("A1B2-C3D4-E5F6-0718");
    }

    [Fact]
    public void RoundTrip_SinMachineId_QuedaPortable()
    {
        var key = LicenseCodec.Encode(Payload(), _keys);

        var decoded = LicenseCodec.TryDecode(key, _keys);

        decoded.Should().Be(Payload());
        decoded!.MachineId.Should().BeNull();
    }

    [Fact]
    public void Encode_SinMachineId_NoEscribeElCampo()
    {
        // Una clave portable queda con el mismo JSON de siempre: ni una versión vieja de la app ni
        // una herramienta externa ven un campo nuevo.
        var key = LicenseCodec.Encode(Payload(), _keys);

        var json = Encoding.UTF8.GetString(PayloadBytes(key));

        json.Should().NotContain("MachineId");
    }

    [Fact]
    public void TryDecode_ClaveEmitidaAntesDelCampo_DevuelveMachineIdNull()
    {
        // JSON de 8 campos, tal como lo firmaban las versiones anteriores del emisor.
        var legacyJson = JsonSerializer.Serialize(new
        {
            LicenseId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            Gym = "Iron Temple SRL",
            Cuit = "30-71234567-1",
            Tier = "Pro",
            MaxSites = 3,
            MaxMembers = 1000,
            IssuedOn = Issued,
            ExpiresOn = Expires,
        });

        var decoded = LicenseCodec.TryDecode(SignedKeyFor(legacyJson), _keys);

        decoded.Should().Be(Payload());
        decoded!.MachineId.Should().BeNull();
    }

    [Fact]
    public void TryDecode_MachineIdAdulterado_InvalidaLaFirma()
    {
        // Quien copie la firma de una clave atada a otra PC no puede cambiarle el MachineId.
        var original = LicenseCodec.Encode(Payload("A1B2-C3D4-E5F6-0718"), _keys);
        var tamperedJson = Encoding.UTF8.GetString(PayloadBytes(original))
            .Replace("A1B2-C3D4-E5F6-0718", "FFFF-FFFF-FFFF-FFFF");
        var signature = original.Split('.')[2];
        var tampered = $"GYMF.{Base64Url(Encoding.UTF8.GetBytes(tamperedJson))}.{signature}";

        LicenseCodec.TryDecode(tampered, _keys).Should().BeNull();
    }

    private static byte[] PayloadBytes(string key)
    {
        var base64 = key.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
    }
}
