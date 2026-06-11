using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace GymForge.Desktop.Converters;

/// <summary>bool activo → color verde/gris (para badges).</summary>
public class ActiveColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.Parse("#2E7D32") : Color.Parse("#757575");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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
