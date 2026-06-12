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

/// <summary>
/// bool éxito → brush verde; false → rojo (mensajes de resultado).
/// Con ConverterParameter="tint" devuelve la versión translúcida (fondo de píldoras).
/// </summary>
public class ActiveColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Tonos medios: legibles tanto sobre tarjeta clara como oscura.
        var color = Color.Parse(value is true ? "#3F9C49" : "#E05252");
        if (parameter is "tint")
            color = new Color(0x22, color.R, color.G, color.B);
        return new SolidColorBrush(color);
    }

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
