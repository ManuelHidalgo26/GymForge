using System.Security.Cryptography;
using System.Text.Json;

namespace GymForge.Application.UseCases.Licensing;

/// <summary>
/// Codifica/decodifica claves de licencia: payload JSON firmado con ECDSA P-256.
/// Formato: GYMF.&lt;base64url(payload)&gt;.&lt;base64url(firma)&gt; — verificable offline.
/// </summary>
public static class LicenseCodec
{
    private const string Prefix = "GYMF";

    public static string Encode(LicensePayload payload, ECDsa privateKey)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload);
        var signature = privateKey.SignData(json, HashAlgorithmName.SHA256);
        return $"{Prefix}.{ToBase64Url(json)}.{ToBase64Url(signature)}";
    }

    /// <summary>Devuelve el payload si la clave es bien formada y la firma válida; si no, null.</summary>
    public static LicensePayload? TryDecode(string? key, ECDsa publicKey)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var parts = key.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != Prefix) return null;

        try
        {
            var json = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);
            if (!publicKey.VerifyData(json, signature, HashAlgorithmName.SHA256)) return null;
            return JsonSerializer.Deserialize<LicensePayload>(json);
        }
        catch (FormatException) { return null; }
        catch (JsonException) { return null; }
    }

    private static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='));
    }
}
