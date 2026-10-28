namespace GymForge.Domain.Enums;

/// <summary>Origen de un movimiento de la cuenta corriente del socio.</summary>
public enum CreditMovementType
{
    /// <summary>Excedente de un pago que no tenía cargos para absorberlo (suma saldo).</summary>
    Overpayment,
    /// <summary>Saldo usado para saldar un cargo (resta saldo).</summary>
    AppliedToCharge,
    /// <summary>Corrección manual, positiva o negativa.</summary>
    Adjustment,
    /// <summary>Devolución del saldo al socio.</summary>
    Refund
}
