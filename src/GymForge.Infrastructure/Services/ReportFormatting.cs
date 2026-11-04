using System.Globalization;
using System.Text.RegularExpressions;
using GymForge.Application.UseCases.Reports;
using QuestPDF.Drawing.Exceptions;

namespace GymForge.Infrastructure.Services;

/// <summary>
/// Formato del negocio compartido por el recibo y los reportes: moneda es-AR ($35.000,00), fechas
/// dd/MM/yyyy, color de marca validado y logo opcional. Única fuente: nadie más formatea a mano.
/// </summary>
public static partial class ReportFormatting
{
    public const string DefaultBrandColor = "#6366F1";
    public const string NoValue = "—";

    private static readonly CultureInfo EsAr = CultureInfo.GetCultureInfo("es-AR");
    private static readonly NumberFormatInfo ArsFormat = BuildArsFormat();

    private static NumberFormatInfo BuildArsFormat()
    {
        var nfi = (NumberFormatInfo)EsAr.NumberFormat.Clone();
        nfi.CurrencySymbol = "$";
        nfi.CurrencyPositivePattern = 0; // $n
        nfi.CurrencyNegativePattern = 1; // -$n
        return nfi;
    }

    public static string Money(decimal value) => value.ToString("C2", ArsFormat);

    /// <summary>Fracción 0..1 como porcentaje: 0,42 → "42%", 0,425 → "42,5%".</summary>
    public static string Percent(decimal fraction) =>
        (fraction * 100).ToString("0.#", EsAr) + "%";

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string DateTimeText(DateTime value) =>
        value.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Texto de una celda tal como va en el PDF; el valor ausente se marca con "—".</summary>
    /// <exception cref="InvalidOperationException">El valor no es del tipo que declara la columna.</exception>
    public static string Cell(ReportColumn column, object? value) => value is null
        ? NoValue
        : column.Type switch
        {
            ReportColumnType.Text => Expect<string>(column, value),
            ReportColumnType.Money => Money(Expect<decimal>(column, value)),
            ReportColumnType.Integer => Expect<int>(column, value).ToString(CultureInfo.InvariantCulture),
            ReportColumnType.Percent => Percent(Expect<decimal>(column, value)),
            ReportColumnType.Date => value switch
            {
                DateOnly date => Date(date),
                DateTime dateTime => DateTimeText(dateTime),
                _ => throw TypeMismatch(column, value),
            },
            _ => throw new InvalidOperationException($"Tipo de columna desconocido: {column.Type}."),
        };

    /// <summary>El valor de la celda como <typeparamref name="T"/>; falla fuerte si el reporte armó un tipo que no es.</summary>
    internal static T Expect<T>(ReportColumn column, object value) =>
        value is T typed ? typed : throw TypeMismatch(column, value);

    private static InvalidOperationException TypeMismatch(ReportColumn column, object value) =>
        new($"La columna '{column.Header}' ({column.Type}) no admite valores {value.GetType().Name}.");

    /// <summary>Hex de marca válido (#RRGGBB) en mayúsculas, o el indigo por defecto.</summary>
    public static string BrandColor(string? hex) =>
        !string.IsNullOrWhiteSpace(hex) && HexColor().IsMatch(hex)
            ? hex.ToUpperInvariant()
            : DefaultBrandColor;

    /// <summary>Los bytes del logo si el archivo existe y es una imagen que se puede dibujar; si no, null (sin logo).</summary>
    public static byte[]? TryLoadLogo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }

        return CanDecode(bytes) ? bytes : null;
    }

    // Un logo corrupto no debe impedir el recibo ni el reporte: QuestPDF falla recién al componer el PDF.
    private static bool CanDecode(byte[] image)
    {
        try
        {
            QuestPDF.Infrastructure.Image.FromBinaryData(image);
            return true;
        }
        catch (DocumentComposeException)
        {
            return false;
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
