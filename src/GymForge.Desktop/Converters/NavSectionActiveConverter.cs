using System.Globalization;
using Avalonia.Data.Converters;
using GymForge.Desktop.Views.Shell;

namespace GymForge.Desktop.Converters;

/// <summary>
/// Compares the current NavSection value against a ConverterParameter string
/// to drive the CSS "active" class on sidebar nav buttons.
/// Usage: Classes.active="{Binding CurrentSection, Converter={StaticResource NavSectionActiveConverter}, ConverterParameter=Members}"
/// </summary>
public sealed class NavSectionActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is NavSection section && parameter is string target &&
        section.ToString().Equals(target, StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
