using GymForge.Domain.Enums;

namespace GymForge.Domain.Entities;

public class Shift : BaseEntity
{
    public Guid? RegisterId { get; private set; }
    public Guid CashierId { get; private set; }
    public Guid SiteId { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public decimal OpeningCash { get; private set; }
    public decimal? ClosingCashDeclared { get; private set; }
    public decimal? ClosingCashSystem { get; private set; }
    public decimal? Difference => ClosingCashDeclared.HasValue && ClosingCashSystem.HasValue
        ? ClosingCashDeclared - ClosingCashSystem
        : null;
    public ShiftStatus Status { get; private set; } = ShiftStatus.Open;
    public string? Notes { get; private set; }

    public ICollection<CashMovement> Movements { get; private set; } = [];

    // Cálculos de arqueo (requieren Movements cargados; ignorados en el mapeo EF).
    public decimal CashIn => Movements
        .Where(m => m.Type is CashMovementType.Income or CashMovementType.Deposit)
        .Sum(m => m.Amount);

    public decimal CashOut => Movements
        .Where(m => m.Type is CashMovementType.Expense or CashMovementType.Withdrawal)
        .Sum(m => m.Amount);

    public decimal ExpectedCash => OpeningCash + CashIn - CashOut;

    private Shift() { }

    public static Shift Open(Guid companyId, Guid siteId, Guid cashierId, decimal openingCash, Guid? registerId = null) =>
        new()
        {
            CompanyId = companyId,
            SiteId = siteId,
            CashierId = cashierId,
            OpeningCash = openingCash,
            RegisterId = registerId,
            OpenedAt = DateTime.UtcNow,
            Status = ShiftStatus.Open
        };

    public void BeginClose(decimal declaredAmount, decimal systemAmount)
    {
        if (Status != ShiftStatus.Open)
            throw new InvalidOperationException("Solo se puede cerrar una caja abierta.");

        Status = ShiftStatus.Closing;
        ClosingCashDeclared = declaredAmount;
        ClosingCashSystem = systemAmount;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ConfirmClose(string? notes = null)
    {
        Status = ShiftStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        Notes = notes;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class CashMovement : BaseEntity
{
    public Guid ShiftId { get; private set; }
    public CashMovementType Type { get; private set; }
    public CashMovementCategory Category { get; private set; }
    public decimal Amount { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public string? Notes { get; private set; }
    public DateTime MovedAt { get; private set; } = DateTime.UtcNow;

    public Shift Shift { get; private set; } = null!;

    private CashMovement() { }

    public static CashMovement Create(
        Guid shiftId,
        CashMovementType type,
        CashMovementCategory category,
        decimal amount,
        Guid? referenceId = null,
        string? notes = null) =>
        new()
        {
            ShiftId = shiftId,
            Type = type,
            Category = category,
            Amount = amount,
            ReferenceId = referenceId,
            Notes = notes
        };
}
