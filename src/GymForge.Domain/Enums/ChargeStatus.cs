namespace GymForge.Domain.Enums;

public enum ChargeStatus
{
    Draft,
    Pending,
    Billed,
    Paid,
    PartiallyPaid,
    Overdue,
    WrittenOff,
    Refunded
}

public enum ConceptType
{
    MembershipFee,
    SignupFee,
    LateFee,
    ProductSale,
    PersonalTraining,
    ClassDropIn,
    Adjustment
}
