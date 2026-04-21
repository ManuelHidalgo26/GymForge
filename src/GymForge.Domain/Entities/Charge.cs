using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Charge : BaseEntity
{
    public Guid MemberId { get; private set; }
    public Guid? MembershipId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SiteId { get; private set; }

    public ConceptType ConceptType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public decimal TaxAmount { get; private set; }
    public decimal TotalAmount => Amount + TaxAmount;
    public DateOnly DueDate { get; private set; }
    public ChargeStatus Status { get; private set; } = ChargeStatus.Pending;

    public string? InvoiceNumber { get; private set; }
    public string? FiscalCae { get; private set; }
    public string? FiscalXmlUrl { get; private set; }

    public decimal AmountPaid { get; private set; }
    public decimal AmountOutstanding => TotalAmount - AmountPaid;

    // Navigation
    public Member Member { get; private set; } = null!;
    public Membership? Membership { get; private set; }
    public ICollection<PaymentAllocation> Allocations { get; private set; } = [];

    private Charge() { }

    public static Charge Create(
        Guid companyId,
        Guid siteId,
        Guid memberId,
        Guid? membershipId,
        ConceptType conceptType,
        string description,
        decimal amount,
        DateOnly dueDate,
        decimal taxAmount = 0m)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (amount <= 0) throw new ArgumentException("Amount must be positive.");

        return new Charge
        {
            CompanyId = companyId,
            SiteId = siteId,
            MemberId = memberId,
            MembershipId = membershipId,
            ConceptType = conceptType,
            Description = description,
            Amount = amount,
            TaxAmount = taxAmount,
            DueDate = dueDate,
            Status = ChargeStatus.Pending
        };
    }

    public void ApplyPayment(decimal paymentAmount)
    {
        if (paymentAmount <= 0) throw new ArgumentException("Payment amount must be positive.");

        AmountPaid += paymentAmount;
        Status = AmountPaid >= TotalAmount ? ChargeStatus.Paid : ChargeStatus.PartiallyPaid;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkOverdue()
    {
        if (Status is ChargeStatus.Paid or ChargeStatus.WrittenOff) return;
        Status = ChargeStatus.Overdue;
        UpdatedAt = DateTime.UtcNow;
    }

    public void WriteOff(string reason)
    {
        Status = ChargeStatus.WrittenOff;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetFiscalData(string invoiceNumber, string cae, string xmlUrl)
    {
        InvoiceNumber = invoiceNumber;
        FiscalCae = cae;
        FiscalXmlUrl = xmlUrl;
        Status = ChargeStatus.Billed;
        UpdatedAt = DateTime.UtcNow;
    }
}
