using Avalonia.Media;

namespace GymForge.Desktop.Services;

/// <summary>Colores de marca resueltos para un tema: tinte de fondo y texto de acento legible sobre él.</summary>
public readonly record struct BrandThemeColors(Color Tint, Color Text);

/// <summary>Paleta derivada del acento del gimnasio para los dos temas, más el color de texto sobre el acento pleno.</summary>
public readonly record struct BrandColors(BrandThemeColors Light, BrandThemeColors Dark, Color OnAccent);

/// <summary>
/// Deriva, a partir del color de marca del gimnasio, los colores que dependen de él: el tinte de fondo
/// (chips, avatares, pills de acento), un tono de texto de acento con contraste AA sobre ese tinte y el
/// color de texto para los botones rellenos con el acento. Así cualquier color (incluso uno claro, como un
/// amarillo) queda legible sin limitar la paleta de swatches.
/// </summary>
public static class BrandPalette
{
    public const byte LightTintAlpha = 0x1F;
    public const byte DarkTintAlpha = 0x2E;

    /// <summary>Contraste mínimo WCAG AA para texto normal.</summary>
    private const double AaNormalText = 4.5;

    /// <summary>Con menos de este contraste el blanco ya no sirve como texto de un botón relleno (texto en negrita).</summary>
    private const double MinWhiteOnAccent = 3.0;

    private const double MixStep = 0.02;

    private static readonly Color OnAccentLight = Colors.White;
    private static readonly Color OnAccentDark = Color.Parse("#111827");

    /// <summary>Superficies sobre las que cae un pill/chip de acento en light: card y lienzo gris.</summary>
    public static readonly IReadOnlyList<Color> LightSurfaces = [Color.Parse("#FFFFFF"), Color.Parse("#F0F0F2")];

    /// <summary>Superficies en dark: lienzo, card y card con panel hundido encima (la más clara).</summary>
    public static readonly IReadOnlyList<Color> DarkSurfaces =
        [Color.Parse("#2C2C2E"), Color.Parse("#3A3A3C"), Color.Parse("#484849")];

    /// <summary>Hex inválido o vacío → índigo por defecto.</summary>
    public static Color Resolve(string? hex) =>
        Color.TryParse(hex, out var color) ? color : Color.Parse(SessionContext.DefaultBrandColorHex);

    public static BrandColors For(Color accent) => new(
        BuildTheme(accent, LightTintAlpha, LightSurfaces, darkTheme: false),
        BuildTheme(accent, DarkTintAlpha, DarkSurfaces, darkTheme: true),
        ContrastRatio(OnAccentLight, accent) >= MinWhiteOnAccent ? OnAccentLight : OnAccentDark);

    /// <summary>Compone un color con alfa sobre un fondo opaco.</summary>
    public static Color Composite(Color foreground, Color background)
    {
        var alpha = foreground.A / 255.0;
        return Color.FromRgb(
            Blend(foreground.R, background.R, alpha),
            Blend(foreground.G, background.G, alpha),
            Blend(foreground.B, background.B, alpha));
    }

    /// <summary>Relación de contraste WCAG 2.x entre dos colores opacos (1 a 21).</summary>
    public static double ContrastRatio(Color a, Color b)
    {
        var (lighter, darker) = Luminance(a) >= Luminance(b) ? (Luminance(a), Luminance(b)) : (Luminance(b), Luminance(a));
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Luminancia relativa WCAG de un color opaco.</summary>
    public static double Luminance(Color color) =>
        0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);

    private static BrandThemeColors BuildTheme(Color accent, byte tintAlpha, IReadOnlyList<Color> surfaces, bool darkTheme)
    {
        var tint = Color.FromArgb(tintAlpha, accent.R, accent.G, accent.B);
        var backgrounds = surfaces.Select(surface => Composite(tint, surface)).ToList();
        return new BrandThemeColors(tint, ReadableText(accent, backgrounds, darkTheme));
    }

    /// <summary>Acerca el acento al negro (light) o al blanco (dark) lo mínimo necesario para llegar a AA en todos los fondos.</summary>
    private static Color ReadableText(Color accent, IReadOnlyList<Color> backgrounds, bool darkTheme)
    {
        var target = darkTheme ? Colors.White : Colors.Black;
        for (var amount = 0.0; amount < 1.0; amount += MixStep)
        {
            var candidate = Mix(accent, target, amount);
            if (backgrounds.All(background => ContrastRatio(candidate, background) >= AaNormalText))
                return candidate;
        }

        return target;
    }

    private static Color Mix(Color from, Color to, double amount) => Color.FromRgb(
        Blend(to.R, from.R, amount),
        Blend(to.G, from.G, amount),
        Blend(to.B, from.B, amount));

    private static byte Blend(byte top, byte bottom, double topWeight) =>
        (byte)Math.Round(top * topWeight + bottom * (1 - topWeight));

    private static double Linear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }
}
