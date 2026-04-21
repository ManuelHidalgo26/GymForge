namespace GymForge.Domain.Enums;

public enum PaymentMethod
{
    Cash,
    CreditCard,
    DebitCard,
    BankTransfer,
    DirectDebit,
    Stripe,
    MercadoPago,
    Voucher,
    AccountCredit,
    Cheque
}

public enum PaymentStatus
{
    Pending,
    Completed,
    Failed,
    Refunded,
    ChargedBack
}
