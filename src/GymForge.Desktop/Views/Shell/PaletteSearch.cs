using System.Globalization;
using System.Text;

namespace GymForge.Desktop.Views.Shell;

/// <summary>Filtro de la paleta: contiene el texto buscado, sin distinguir mayúsculas ni acentos (sin ranking fuzzy).</summary>
public static class PaletteSearch
{
    public static IReadOnlyList<PaletteCommand> Filter(IEnumerable<PaletteCommand> commands, string? query)
    {
        var needle = Normalize(query);
        return commands
            .Where(command => needle.Length == 0 || Normalize(command.Title).Contains(needle, StringComparison.Ordinal))
            .ToList();
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark))
            builder.Append(ch);
        return builder.ToString();
    }
}
