using System.ComponentModel.DataAnnotations;
using FluentValidation;

namespace GymForge.Application.UseCases.Members;

/// <summary>Reglas de datos del socio compartidas por el alta y la edición, para que no diverjan.</summary>
internal static class MemberValidationRules
{
    /// <summary>Largo de las columnas de <c>Members</c> (ver MemberConfiguration).</summary>
    private const int NameMaxLength = 100;
    private const int DocumentMaxLength = 20;
    private const int EmailMaxLength = 254;
    private const int PhoneMaxLength = 30;

    private static readonly EmailAddressAttribute EmailFormat = new();

    public static IRuleBuilderOptions<T, string> ValidName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(NameMaxLength);

    public static IRuleBuilderOptions<T, string> ValidDocumentNumber<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(DocumentMaxLength);

    public static IRuleBuilderOptions<T, string?> ValidEmail<T>(this IRuleBuilder<T, string?> rule) =>
        rule.Must(email => string.IsNullOrEmpty(email) || (email.Length <= EmailMaxLength && EmailFormat.IsValid(email)))
            .WithMessage("El email no es válido.");

    public static IRuleBuilderOptions<T, string?> ValidPhone<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(PhoneMaxLength);

    public static IRuleBuilderOptions<T, DateOnly?> ValidBirthDate<T>(this IRuleBuilder<T, DateOnly?> rule) =>
        rule.Must(date => !date.HasValue || date.Value <= DateOnly.FromDateTime(DateTime.Today.AddYears(-5)))
            .WithMessage("La fecha de nacimiento parece incorrecta.");
}
