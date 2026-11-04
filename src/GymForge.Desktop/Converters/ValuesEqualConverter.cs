using System.Globalization;
using Avalonia.Data.Converters;

namespace GymForge.Desktop.Converters;

/// <summary>true si los dos valores enlazados son iguales (igualdad de valor, p. ej. records).
/// Se usa para resaltar el horario seleccionado en la grilla de Clases.</summary>
public class ValuesEqualConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count == 2 && values[0] is { } first && Equals(first, values[1]);
}
