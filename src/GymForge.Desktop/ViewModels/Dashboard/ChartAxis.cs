using System.Globalization;

namespace GymForge.Desktop.ViewModels.Dashboard;

/// <summary>Una referencia del eje Y: monto, posición relativa (0 = base, 1 = tope) y etiqueta formateada.</summary>
public record AxisTick(decimal Value, double Fraction, string Label);

/// <summary>Eje Y resuelto: el tope redondeado (contra el que se escalan las barras) y sus referencias.</summary>
public record AxisScale(decimal Top, IReadOnlyList<AxisTick> Ticks);

/// <summary>Cálculo del eje Y del gráfico de recaudación: tope y paso "lindos" (1, 2, 2,5, 5 × 10^n), 3 a 4 intervalos.</summary>
public static class ChartAxis
{
    private const int TargetIntervals = 3;
    private static readonly decimal[] NiceSteps = [1m, 2m, 2.5m, 5m, 10m];

    public static AxisScale Build(decimal maxAmount, CultureInfo? culture = null)
    {
        var format = culture ?? CultureInfo.CurrentCulture;
        if (maxAmount <= 0)
            return new AxisScale(0m, [new AxisTick(0m, 0.0, Label(0m, format))]);

        var step = NiceStep(maxAmount / TargetIntervals);
        var top = Math.Ceiling(maxAmount / step) * step;
        var ticks = Enumerable.Range(0, (int)(top / step) + 1)
            .Select(i => i * step)
            .Select(value => new AxisTick(value, (double)(value / top), Label(value, format)))
            .ToList();
        return new AxisScale(top, ticks);
    }

    /// <summary>Menor paso entero de la serie 1-2-2,5-5-10 (× potencia de 10) que alcanza el paso crudo.</summary>
    private static decimal NiceStep(decimal rawStep)
    {
        rawStep = Math.Max(rawStep, 1m); // el eje habla de pesos enteros
        var magnitude = 1m;
        while (rawStep / magnitude >= 10m) magnitude *= 10m;

        return NiceSteps
            .Select(step => step * magnitude)
            .First(step => step >= rawStep && step == decimal.Truncate(step));
    }

    private static string Label(decimal value, CultureInfo culture) => "$" + value.ToString("N0", culture);
}
