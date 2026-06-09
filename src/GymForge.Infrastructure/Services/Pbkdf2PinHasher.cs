using System.Security.Cryptography;
using GymForge.Application.Interfaces;

namespace GymForge.Infrastructure.Services;

/// <summary>PBKDF2-SHA256 con salt aleatorio por PIN. Formato: "iterations.salt.key" en base64.</summary>
public sealed class Pbkdf2PinHasher : IPinHasher
{
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algo = HashAlgorithmName.SHA256;

    public string Hash(string pin)
    {
        ArgumentException.ThrowIfNullOrEmpty(pin);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, Algo, KeySize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    public bool Verify(string pin, string hash)
    {
        if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash)) return false;

        var parts = hash.Split('.', 3);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;

        byte[] salt, key;
        try
        {
            salt = Convert.FromBase64String(parts[1]);
            key = Convert.FromBase64String(parts[2]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] attempt = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, Algo, key.Length);
        return CryptographicOperations.FixedTimeEquals(attempt, key);
    }
}
