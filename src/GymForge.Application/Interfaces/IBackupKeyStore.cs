namespace GymForge.Application.Interfaces;

/// <summary>Reglas de la frase de acceso del respaldo (la que cifra lo que sale de la PC).</summary>
public static class BackupPassphrase
{
    public const int MinLength = 12;
}

/// <summary>
/// Guarda en ESTA PC la frase de acceso del respaldo para cifrar la copia externa sin preguntar (corre sola).
/// No es necesaria para restaurar: ahí se re-deriva la clave de la frase que tipea el usuario. Nunca viaja
/// con el export y la UI nunca la muestra: <see cref="GetPassphrase"/> es solo para el cifrado.
/// </summary>
public interface IBackupKeyStore
{
    bool HasPassphrase { get; }

    /// <summary>La frase fijada, o null si no hay (o no se puede leer en esta PC). Uso interno del cifrado.</summary>
    string? GetPassphrase();

    /// <exception cref="ArgumentException">La frase es más corta que <see cref="BackupPassphrase.MinLength"/>.</exception>
    void SetPassphrase(string passphrase);
}
