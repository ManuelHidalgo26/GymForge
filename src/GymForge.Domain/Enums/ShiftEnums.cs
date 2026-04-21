namespace GymForge.Domain.Enums;

public enum ShiftStatus
{
    Open,
    Closing,
    Closed,
    Adjusted
}

public enum CashMovementType
{
    Income,
    Expense,
    Withdrawal,
    Deposit
}

public enum CashMovementCategory
{
    Sale,
    Membership,
    Refund,
    PettyCash,
    SupplierPayment,
    Salary
}
