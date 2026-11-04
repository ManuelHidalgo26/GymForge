using GymForge.Domain.Enums;

namespace GymForge.Application.Services;

/// <summary>
/// Cómo se nombran en español, en recibos y reportes, los enums del dominio. Una sola fuente para los
/// documentos que genera la capa de aplicación; la UI tiene su propio conversor (EnumDisplayConverter).
/// </summary>
public static class SpanishLabels
{
    public static string For(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "Efectivo",
        PaymentMethod.CreditCard => "Tarjeta de crédito",
        PaymentMethod.DebitCard => "Tarjeta de débito",
        PaymentMethod.BankTransfer => "Transferencia",
        PaymentMethod.DirectDebit => "Débito automático",
        PaymentMethod.MercadoPago => "Mercado Pago",
        PaymentMethod.Voucher => "Voucher",
        PaymentMethod.AccountCredit => "Saldo a favor",
        PaymentMethod.Cheque => "Cheque",
        _ => method.ToString(),
    };

    /// <summary>
    /// Concepto de un cargo cuando no hay un plan con el que nombrarlo: las cuotas de membresía se nombran por plan,
    /// así que "Cuotas" es solo para las que no lo tienen (cargos anteriores a que se vincularan a su membresía).
    /// </summary>
    public static string For(ConceptType concept) => concept switch
    {
        ConceptType.MembershipFee => "Cuotas (sin plan asignado)",
        ConceptType.SignupFee => "Inscripciones",
        ConceptType.LateFee => "Recargos por mora",
        ConceptType.ProductSale => "Productos",
        ConceptType.PersonalTraining => "Personal training",
        ConceptType.ClassDropIn => "Clases sueltas",
        ConceptType.Adjustment => "Ajustes",
        _ => concept.ToString(),
    };
}
