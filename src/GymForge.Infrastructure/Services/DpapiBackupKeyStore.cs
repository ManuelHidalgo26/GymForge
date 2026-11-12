using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using GymForge.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Frase de acceso del respaldo protegida con DPAPI (usuario actual de Windows) en <c>vendor\backup-pass.dpapi</c>,
/// junto a los demás secretos locales que no salen de la PC. Otro usuario de Windows u otra PC no pueden leerla.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DpapiBackupKeyStore : IBackupKeyStore
{
    private readonly string _path;
    private readonly ILogger<DpapiBackupKeyStore> _logger;

    public DpapiBackupKeyStore(ILogger<DpapiBackupKeyStore> logger, string? path = null)
    {
        _logger = logger;
        _path = path ?? DefaultPath;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GymForge", "vendor", "backup-pass.dpapi");

    public bool HasPassphrase => File.Exists(_path);

    public string? GetPassphrase()
    {
        if (!File.Exists(_path)) return null;

        byte[]? secret = null;
        try
        {
            secret = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secret);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // Perfil de Windows distinto o archivo dañado: se trata como "sin frase" y se pide fijarla de nuevo.
            _logger.LogWarning(ex, "No se pudo leer la frase de acceso del respaldo guardada en esta PC.");
            return null;
        }
        finally
        {
            if (secret is not null) CryptographicOperations.ZeroMemory(secret);
        }
    }

    public void SetPassphrase(string passphrase)
    {
        passphrase = BackupPassphrase.Normalize(passphrase);
        if (passphrase.Length < BackupPassphrase.MinLength)
            throw new ArgumentException(
                $"La frase de acceso debe tener al menos {BackupPassphrase.MinLength} caracteres.", nameof(passphrase));

        // NEW-56: la frase viene del UI como string administrado y no se puede poner en cero; solo los bytes
        // derivados (secret) se limpian. Limitación aceptada: evitarla exigiría SecureString/char[] desde la UI.
        var secret = Encoding.UTF8.GetBytes(passphrase);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }
}
