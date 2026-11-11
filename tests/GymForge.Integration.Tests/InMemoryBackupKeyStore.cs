using GymForge.Application.Interfaces;

namespace GymForge.Integration.Tests;

/// <summary>Frase de acceso del respaldo en memoria, para los tests que no necesitan DPAPI.</summary>
internal sealed class InMemoryBackupKeyStore(string? passphrase = null) : IBackupKeyStore
{
    public const string TestPassphrase = "frase de prueba bien larga";

    private string? _passphrase = passphrase;

    public bool HasPassphrase => _passphrase is not null;

    public string? GetPassphrase() => _passphrase;

    public void SetPassphrase(string passphrase) => _passphrase = passphrase;
}
