using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Payment : BaseEntity
{
    // Nullable: una venta a consumidor final (no socio) genera un pago sin socio.
    public Guid? MemberId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }
    // Venta de producto asociada (si el pago corresponde a una venta del POS).
    public Guid? SaleId { get; private set; }
    public decimal Amount { get; private set; }
    public PaymentMethod Method { get; private set; }
    public string? Processor { get; private set; }
    public string? ProcessorTxId { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? CardBrand { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public Guid? ShiftId { get; private set; }
    public Guid CashierId { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.Completed;
    public string? Notes { get; private set; }

    public Member? Member { get; private set; }
    public ICollection<PaymentAllocation> Allocations { get; private set; } = [];

    private Payment() { }

    public static Payment Create(
        Guid companyId,
        Guid siteId,
        Guid? memberId,
        Guid cashierId,
        decimal amount,
        PaymentMethod method,
        Guid? shiftId = null,
        string? cardLast4 = null,
        string? cardBrand = null,
        string? processorTxId = null,
        Guid? saleId = null)
    {
        if (amount <= 0) throw new ArgumentException("Payment amount must be positive.");

        return new Payment
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            CashierId = cashierId,
            Amount = amount,
            Method = method,
            ShiftId = shiftId,
            CardLast4 = cardLast4,
            CardBrand = cardBrand,
            ProcessorTxId = processorTxId,
            SaleId = saleId,
            ReceivedAt = DateTime.UtcNow
        };
    }

    public void Refund()
    {
        Status = PaymentStatus.Refunded;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class PaymentAllocation : BaseEntity
{
    public Guid PaymentId { get; private set; }
    public Guid ChargeId { get; private set; }
    public decimal Amount { get; private set; }

    public Payment Payment { get; private set; } = null!;
    public Charge Charge { get; private set; } = null!;

    private PaymentAllocation() { }

    public static PaymentAllocation Create(Guid paymentId, Guid chargeId, decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Allocation amount must be positive.");
        return new PaymentAllocation { PaymentId = paymentId, ChargeId = chargeId, Amount = amount };
    }
}
