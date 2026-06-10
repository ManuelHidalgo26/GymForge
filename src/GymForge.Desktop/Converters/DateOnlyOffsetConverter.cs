using System.Globalization;
using Avalonia.Data.Converters;

namespace GymForge.Desktop.Converters;

/// <summary>Puentea DateOnly? (modelo) con DateTimeOffset? (DatePicker de Avalonia).</summary>
public class DateOnlyOffsetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly d
            ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
            : (DateTimeOffset?)null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset dto ? DateOnly.FromDateTime(dto.Date) : (DateOnly?)null;
}
