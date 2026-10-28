using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

/// <summary>
/// Movimiento de la cuenta corriente del socio. Libro mayor append-only: nunca se hace UPDATE ni
/// DELETE; el saldo a favor es la suma de los montos (positivo agrega crédito, negativo lo consume).
/// No implementa IAuditable a propósito: el ledger es su propia auditoría.
/// </summary>
public class MemberCreditMovement : BaseEntity, ISiteScoped
{
    public Guid MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    public decimal Amount { get; private set; }
    public CreditMovementType Type { get; private set; }
    /// <summary>Pago de origen (excedente) o pago que aplica el crédito.</summary>
    public Guid? PaymentId { get; private set; }
    /// <summary>Cargo saldado al consumir crédito.</summary>
    public Guid? ChargeId { get; private set; }
    public string? Note { get; private set; }

    private MemberCreditMovement() { }

    public static MemberCreditMovement CreditFromOverpayment(
        Guid companyId, Guid siteId, Guid memberId, decimal amount, Guid paymentId)
    {
        if (amount <= 0) throw new ArgumentException("El crédito debe ser positivo.", nameof(amount));

        return new MemberCreditMovement
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            Amount = amount,
            Type = CreditMovementType.Overpayment,
            PaymentId = paymentId
        };
    }

    public static MemberCreditMovement DebitForCharge(
        Guid companyId, Guid siteId, Guid memberId, decimal amount, Guid chargeId)
    {
        if (amount <= 0) throw new ArgumentException("El consumo debe ser positivo.", nameof(amount));

        return new MemberCreditMovement
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            Amount = -amount,
            Type = CreditMovementType.AppliedToCharge,
            ChargeId = chargeId
        };
    }

    public static MemberCreditMovement Adjustment(
        Guid companyId, Guid siteId, Guid memberId, decimal signedAmount, string note)
    {
        if (signedAmount == 0) throw new ArgumentException("El ajuste no puede ser cero.", nameof(signedAmount));
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        return new MemberCreditMovement
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            Amount = signedAmount,
            Type = CreditMovementType.Adjustment,
            Note = note
        };
    }
}
