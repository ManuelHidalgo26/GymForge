using Avalonia.Data.Converters;
using System.Globalization;

namespace GymForge.Desktop.Converters;

/// <summary>Converts sidebar collapsed boolean → width (64 collapsed / 240 expanded).</summary>
public class SidebarWidthConverter : IValueConverter
{
    public static readonly SidebarWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? 64.0 : 240.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Converts collapsed bool → Fluent icon symbol string.</summary>
public class CollapsedIconConverter : IValueConverter
{
    public static readonly CollapsedIconConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        // When collapsed show "ChevronRight", when expanded show "ChevronLeft"
        value is true ? "ChevronRight" : "ChevronLeft";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
