namespace GymForge.Desktop.Controls;

/// <summary>Edición del texto del teclado numérico en pantalla. Pura (sin Avalonia): la usa <see cref="NumericKeypad"/>.</summary>
public static class KeypadText
{
    private const char MaskChar = '●';

    /// <summary>Agrega un dígito al final, salvo que no sea un dígito o el texto ya esté en el largo máximo.</summary>
    public static string Append(string text, char digit, int maxLength) =>
        char.IsAsciiDigit(digit) && text.Length < maxLength ? text + digit : text;

    /// <summary>Borra el último carácter (no hace nada si no hay).</summary>
    public static string Backspace(string text) => text.Length == 0 ? text : text[..^1];

    /// <summary>Lo que muestra el visor: el texto tal cual o, para un PIN, un punto por carácter.</summary>
    public static string Display(string text, bool mask) => mask ? new string(MaskChar, text.Length) : text;
}
