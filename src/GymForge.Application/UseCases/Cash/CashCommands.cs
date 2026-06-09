using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Cash;

// ── Abrir caja ────────────────────────────────────────────────────────────────

public record OpenShiftCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid CashierId,
    decimal OpeningCash) : IRequest<ShiftDto>;

public class OpenShiftCommandValidator : AbstractValidator<OpenShiftCommand>
{
    public OpenShiftCommandValidator() =>
        RuleFor(x => x.OpeningCash).GreaterThanOrEqualTo(0).WithMessage("El fondo inicial no puede ser negativo.");
}

public class OpenShiftCommandHandler : IRequestHandler<OpenShiftCommand, ShiftDto>
{
    private readonly IShiftRepository _repo;
    public OpenShiftCommandHandler(IShiftRepository repo) => _repo = repo;

    public async Task<ShiftDto> Handle(OpenShiftCommand cmd, CancellationToken ct)
    {
        // Una sola caja abierta por sede.
        if (await _repo.GetOpenForSiteAsync(cmd.SiteId, ct) is not null)
            throw new InvalidOperationException("Ya hay una caja abierta en esta sede.");

        var shift = Shift.Open(cmd.CompanyId, cmd.SiteId, cmd.CashierId, cmd.OpeningCash);
        await _repo.AddAsync(shift, ct);
        await _repo.SaveChangesAsync(ct);
        return ShiftDto.FromEntity(shift);
    }
}

// ── Movimiento de caja (ingreso/egreso) ──────────────────────────────────────

public record AddCashMovementCommand(
    Guid ShiftId,
    CashMovementType Type,
    CashMovementCategory Category,
    decimal Amount,
    string? Notes = null) : IRequest<ShiftDto>;

public class AddCashMovementCommandValidator : AbstractValidator<AddCashMovementCommand>
{
    public AddCashMovementCommandValidator() =>
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("El monto debe ser positivo.");
}

public class AddCashMovementCommandHandler : IRequestHandler<AddCashMovementCommand, ShiftDto>
{
    private readonly IShiftRepository _repo;
    public AddCashMovementCommandHandler(IShiftRepository repo) => _repo = repo;

    public async Task<ShiftDto> Handle(AddCashMovementCommand cmd, CancellationToken ct)
    {
        var shift = await _repo.GetByIdAsync(cmd.ShiftId, ct)
            ?? throw new InvalidOperationException("La caja indicada no existe.");

        if (shift.Status != ShiftStatus.Open)
            throw new InvalidOperationException("La caja no está abierta.");

        shift.Movements.Add(CashMovement.Create(shift.Id, cmd.Type, cmd.Category, cmd.Amount, notes: cmd.Notes));
        _repo.Update(shift);
        await _repo.SaveChangesAsync(ct);
        return ShiftDto.FromEntity(shift);
    }
}

// ── Cerrar caja (arqueo) ──────────────────────────────────────────────────────

public record CloseShiftCommand(
    Guid ShiftId,
    decimal DeclaredCash,
    string? Notes = null) : IRequest<ShiftDto>;

public class CloseShiftCommandValidator : AbstractValidator<CloseShiftCommand>
{
    public CloseShiftCommandValidator() =>
        RuleFor(x => x.DeclaredCash).GreaterThanOrEqualTo(0).WithMessage("El efectivo declarado no puede ser negativo.");
}

public class CloseShiftCommandHandler : IRequestHandler<CloseShiftCommand, ShiftDto>
{
    private readonly IShiftRepository _repo;
    public CloseShiftCommandHandler(IShiftRepository repo) => _repo = repo;

    public async Task<ShiftDto> Handle(CloseShiftCommand cmd, CancellationToken ct)
    {
        var shift = await _repo.GetByIdAsync(cmd.ShiftId, ct)
            ?? throw new InvalidOperationException("La caja indicada no existe.");

        // El sistema espera ExpectedCash; la diferencia surge contra lo declarado.
        shift.BeginClose(cmd.DeclaredCash, shift.ExpectedCash);
        shift.ConfirmClose(cmd.Notes);
        _repo.Update(shift);
        await _repo.SaveChangesAsync(ct);
        return ShiftDto.FromEntity(shift);
    }
}
