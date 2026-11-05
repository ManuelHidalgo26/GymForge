using System.Globalization;
using Avalonia.Data.Converters;

namespace GymForge.Desktop.Converters;

/// <summary>bool activo → "Activo"/"Inactivo".</summary>
public class ActiveLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Activo" : "Inactivo";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool activo → acción contraria ("Desactivar"/"Activar").</summary>
public class ActiveToggleLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Desactivar" : "Activar";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true si el string enlazado equivale al ConverterParameter (case-insensitive).
/// Se usa para marcar el swatch de color seleccionado en la paleta de marca.</summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value as string, parameter as string, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
