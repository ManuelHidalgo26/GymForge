using FluentValidation;
using GymForge.Domain.ValueObjects;

namespace GymForge.Application.UseCases.Commissions;

/// <summary>Regla de validación del porcentaje de comisión (0 a 100), compartida por staff y producto.</summary>
internal static class CommissionPctRule
{
    public static IRuleBuilderOptions<T, decimal> ValidCommissionPct<T>(this IRuleBuilder<T, decimal> rule) =>
        rule.Must(Commission.IsValidPct).WithMessage("La comisión es un porcentaje de 0 a 100.");
}
