namespace GymForge.Domain.ValueObjects;

/// <summary>
/// Regla única de comisiones del staff. El porcentaje se expresa de 0 a 100 (10 = 10%), coherente con las
/// columnas DECIMAL(5,2). El monto se redondea a centavos hacia afuera (es-AR) y se congela al vender.
/// </summary>
public static class Commission
{
    public const decimal MaxPct = 100m;

    public static bool IsValidPct(decimal pct) => pct is >= 0m and <= MaxPct;

    public static void EnsureValidPct(decimal pct, string paramName)
    {
        if (!IsValidPct(pct))
            throw new ArgumentOutOfRangeException(paramName, pct, "El porcentaje de comisión va de 0 a 100.");
    }

    public static decimal Calculate(decimal baseAmount, decimal pct)
    {
        EnsureValidPct(pct, nameof(pct));
        return Math.Round(baseAmount * pct / 100m, 2, MidpointRounding.AwayFromZero);
    }
}
