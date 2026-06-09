using GymForge.Domain.Entities;
using GymForge.Domain.Enums;

namespace GymForge.Application.DTOs;

public record ShiftDto(
    Guid Id,
    Guid SiteId,
    Guid CashierId,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    decimal OpeningCash,
    decimal CashIn,
    decimal CashOut,
    decimal ExpectedCash,
    decimal? DeclaredCash,
    decimal? Difference,
    ShiftStatus Status,
    IReadOnlyList<CashMovementDto> Movements)
{
    public static ShiftDto FromEntity(Shift s) => new(
        s.Id, s.SiteId, s.CashierId, s.OpenedAt, s.ClosedAt,
        s.OpeningCash, s.CashIn, s.CashOut, s.ExpectedCash,
        s.ClosingCashDeclared, s.Difference, s.Status,
        s.Movements
            .OrderBy(m => m.MovedAt)
            .Select(CashMovementDto.FromEntity)
            .ToList());
}

public record CashMovementDto(
    Guid Id,
    CashMovementType Type,
    CashMovementCategory Category,
    decimal Amount,
    DateTime MovedAt,
    string? Notes)
{
    public static CashMovementDto FromEntity(CashMovement m) =>
        new(m.Id, m.Type, m.Category, m.Amount, m.MovedAt, m.Notes);
}
