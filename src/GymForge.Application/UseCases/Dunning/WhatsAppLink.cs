namespace GymForge.Application.UseCases.Dunning;

/// <summary>
/// Link <c>wa.me</c> para abrir WhatsApp con el mensaje prearmado (enfoque gratis: lo abre el operador y
/// toca "enviar"; no es la API paga).
/// </summary>
/// <remarks>
/// La normalización del celular es BEST-EFFORT y aproximada. La numeración móvil argentina es ambigua (el
/// 15 puede ir antes o después del código de área, el 0 inicial es opcional) y no hay forma infalible sin
/// una librería tipo libphonenumber. Un número mal cargado puede producir un link incorrecto que el
/// operador corrige a mano; sin código de área no se puede armar un número completo. Las reglas del 0 y
/// del 15 son propias de Argentina: <c>countryCode</c> y <c>mobilePrefix</c> solo cambian los prefijos.
/// Recomendación de producto: cargar los celulares en formato internacional.
/// </remarks>
public static class WhatsAppLink
{
    private const string DefaultCountryCode = "54";
    private const string DefaultMobilePrefix = "9";

    // Un número nacional completo son 10 dígitos: área (2-4) + abonado (6-8).
    private const int NationalNumberLength = 10;
    private const string LegacyMobileMarker = "15";

    // E.164: un número internacional tiene a lo sumo 15 dígitos. Más es un campo con dos números cargados o basura.
    private const int MaxInternationalDigits = 15;

    /// <summary>https://wa.me/&lt;dígitos&gt;?text=&lt;mensaje url-encoded&gt;. Lanza si el teléfono no tiene dígitos usables.</summary>
    public static string Build(
        string rawPhone, string message,
        string countryCode = DefaultCountryCode, string mobilePrefix = DefaultMobilePrefix)
    {
        var digits = ToWhatsAppDigits(rawPhone, countryCode, mobilePrefix)
            ?? throw new ArgumentException("El teléfono no tiene dígitos usables.", nameof(rawPhone));

        return $"https://wa.me/{digits}?text={Uri.EscapeDataString(message)}";
    }

    /// <summary>
    /// Normaliza un celular a los dígitos que pide wa.me (54 + 9 + área + número, sin 0 ni 15).
    /// Devuelve null si no quedan dígitos usables o el resultado pasa los 15 dígitos de E.164. Ver las
    /// limitaciones en las observaciones de la clase.
    /// </summary>
    public static string? ToWhatsAppDigits(
        string? rawPhone, string countryCode = DefaultCountryCode, string mobilePrefix = DefaultMobilePrefix)
    {
        var digits = new string((rawPhone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

        var national = digits.StartsWith(countryCode, StringComparison.Ordinal)
            ? digits[countryCode.Length..]
            : digits;

        national = RemoveTrunkPrefix(national);
        national = RemoveMobilePrefix(national, mobilePrefix);
        national = RemoveLegacyMobileMarker(national);

        if (national.Length == 0) return null;

        var full = countryCode + mobilePrefix + national;
        return full.Length > MaxInternationalDigits ? null : full;
    }

    /// <summary>El 0 inicial es el prefijo troncal de las llamadas dentro del país.</summary>
    private static string RemoveTrunkPrefix(string national) =>
        national.StartsWith('0') ? national[1..] : national;

    /// <summary>Un número de más de 10 dígitos que arranca con el prefijo móvil ya lo trae (formato internacional).</summary>
    private static string RemoveMobilePrefix(string national, string mobilePrefix) =>
        national.Length > NationalNumberLength && national.StartsWith(mobilePrefix, StringComparison.Ordinal)
            ? national[mobilePrefix.Length..]
            : national;

    /// <summary>
    /// El 15 de los celulares viejos va al inicio (sin área) o entre el área y el abonado. En este último caso
    /// el número queda en 12 dígitos: se busca el 15 donde puede terminar un código de área (11, o 3-4 dígitos).
    /// </summary>
    private static string RemoveLegacyMobileMarker(string national)
    {
        if (national.StartsWith(LegacyMobileMarker, StringComparison.Ordinal))
            return national[LegacyMobileMarker.Length..];

        if (national.Length != NationalNumberLength + LegacyMobileMarker.Length)
            return national;

        foreach (var areaLength in PossibleAreaLengths(national))
            if (string.CompareOrdinal(national, areaLength, LegacyMobileMarker, 0, LegacyMobileMarker.Length) == 0)
                return national.Remove(areaLength, LegacyMobileMarker.Length);

        return national;
    }

    /// <summary>El código 11 (AMBA) es el único de 2 dígitos; el resto tiene 3 o 4.</summary>
    private static int[] PossibleAreaLengths(string national) =>
        national.StartsWith("11", StringComparison.Ordinal) ? [2] : [3, 4];
}
