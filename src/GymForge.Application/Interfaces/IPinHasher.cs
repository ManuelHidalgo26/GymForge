namespace GymForge.Application.Interfaces;

/// <summary>Hashing de PIN de staff. La implementación usa PBKDF2 (salt por PIN).</summary>
public interface IPinHasher
{
    string Hash(string pin);
    bool Verify(string pin, string hash);
}
