using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace GymForge.Application.UseCases.Licensing;

/// <summary>
/// Código corto de una PC (<c>XXXX-XXXX-XXXX-XXXX</c>, 64 bits en hexadecimal) que se firma en la
/// licencia para atarla a esa máquina. Lo calcula la app a partir de un identificador del sistema y
/// lo valida/normaliza el emisor de claves: ambos comparten estas reglas para coincidir siempre.
/// </summary>
public static class MachineCode
{
    private const string HashDomain = "GymForge.MachineCode.v1:";
    private const int HexDigits = 16;
    private const int GroupSize = 4;

    /// <summary>
    /// Deriva el código a partir del identificador crudo de la PC. El prefijo de dominio evita que el
    /// código sea un hash "desnudo" del identificador del sistema, y no se debe cambiar: invalidaría
    /// toda licencia ya atada.
    /// </summary>
    public static string FromSource(string rawId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(HashDomain + rawId));
        return Format(Convert.ToHexString(hash.AsSpan(0, HexDigits / 2)));
    }

    /// <summary>
    /// Acepta el código tal como lo dicta o pega una persona (minúsculas, con o sin guiones o espacios)
    /// y lo devuelve en formato canónico. False si no son 16 dígitos hexadecimales.
    /// </summary>
    public static bool TryNormalize(string? text, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hex = new string(text.Where(c => c != '-' && !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (hex.Length != HexDigits || !hex.All(Uri.IsHexDigit)) return false;

        code = Format(hex);
        return true;
    }

    private static string Format(string hex) =>
        string.Join('-', Enumerable.Range(0, HexDigits / GroupSize).Select(i => hex.Substring(i * GroupSize, GroupSize)));
}
