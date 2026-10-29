using System.Globalization;
using System.Text.RegularExpressions;

namespace GymForge.Application.UseCases.Dunning;

/// <summary>Datos con los que se rellena la plantilla de un recordatorio de cobro.</summary>
public readonly record struct DunningContext(
    string FirstName, string GymName, decimal Outstanding, DateOnly DueDate, int DaysOverdue);

/// <summary>
/// Arma el texto de un recordatorio a partir de la plantilla editable de la etapa. Lo comparten el job
/// automático y el link de WhatsApp, así el socio recibe el mismo mensaje por los dos caminos.
/// Placeholders: <c>{nombre}</c>, <c>{gimnasio}</c>, <c>{monto}</c>, <c>{vencimiento}</c>, <c>{dias}</c>.
/// Uno desconocido se deja literal: un typo del gimnasio no debe romper el mensaje.
/// </summary>
public static partial class DunningMessageRenderer
{
    // Formato del negocio: "$35.000" (es-AR con el símbolo pegado al número).
    private static readonly NumberFormatInfo ArsFormat = BuildArsFormat();

    private static NumberFormatInfo BuildArsFormat()
    {
        var nfi = (NumberFormatInfo)CultureInfo.GetCultureInfo("es-AR").NumberFormat.Clone();
        nfi.CurrencySymbol = "$";
        nfi.CurrencyPositivePattern = 0; // $n
        return nfi;
    }

    public static string Render(string template, DunningContext ctx) =>
        PlaceholderPattern().Replace(template, match => Resolve(match, ctx));

    private static string Resolve(Match match, DunningContext ctx) => match.Groups[1].Value switch
    {
        "nombre" => ctx.FirstName,
        "gimnasio" => ctx.GymName,
        "monto" => ctx.Outstanding.ToString("C0", ArsFormat),
        "vencimiento" => ctx.DueDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        "dias" => ctx.DaysOverdue.ToString(CultureInfo.InvariantCulture),
        _ => match.Value,
    };

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderPattern();
}
