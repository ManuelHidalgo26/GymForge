using System.Globalization;
using Avalonia.Data.Converters;
using GymForge.Domain.Enums;

namespace GymForge.Desktop.Converters;

/// <summary>
/// Estado de socio (<see cref="MemberStatus"/>), de membresía (<see cref="MembershipStatus"/>), de cobro
/// (<see cref="ChargeStatus"/>) o un bool "activo" → ¿pertenece al tono semántico pedido en
/// <c>ConverterParameter</c>? Alimenta las clases de <c>Border.Pill</c> (success, warning, danger, info,
/// neutral, accent), que ya resuelven fondo y texto con los tokens de Colors.axaml en light y dark (AA).
/// Uso: <c>Classes.success="{Binding Status, Converter={StaticResource StatusTone}, ConverterParameter=success}"</c>.
/// Un estado cae en un solo tono, así que nunca hay dos clases de tono activas a la vez.
/// </summary>
public class StatusToneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        parameter is string tone && string.Equals(ToneOf(value), tone, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string ToneOf(object? status) => status switch
    {
        MemberStatus member => member switch
        {
            MemberStatus.Active => "success",
            MemberStatus.Overdue => "warning",
            MemberStatus.Expired => "danger",
            MemberStatus.Suspended => "danger",
            MemberStatus.Frozen => "info",
            MemberStatus.Prospect => "accent",
            _ => "neutral"
        },
        MembershipStatus membership => membership switch
        {
            MembershipStatus.Active => "success",
            MembershipStatus.Overdue => "warning",
            MembershipStatus.Expired => "danger",
            MembershipStatus.Suspended => "danger",
            MembershipStatus.Frozen => "info",
            MembershipStatus.Trial => "accent",
            _ => "neutral"
        },
        ChargeStatus charge => charge switch
        {
            ChargeStatus.Paid => "success",
            ChargeStatus.Pending => "warning",
            ChargeStatus.PartiallyPaid => "warning",
            ChargeStatus.Overdue => "warning",
            ChargeStatus.Billed => "info",
            ChargeStatus.Refunded => "danger",
            _ => "neutral"
        },
        bool active => active ? "success" : "neutral",
        _ => "neutral"
    };
}
