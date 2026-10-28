using FluentValidation;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Charges;

/// <summary>
/// Usa el saldo a favor del socio para saldar cargos (los indicados o, si no, los pendientes
/// del más antiguo al más nuevo). No es un cobro: no genera Payment ni toca la caja.
/// </summary>
public record ApplyMemberCreditCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid MemberId,
    IReadOnlyList<Guid>? ChargeIds = null) : IRequest<ApplyCreditResult>;

public readonly record struct ApplyCreditResult(decimal Applied, decimal RemainingCredit);

public class ApplyMemberCreditCommandValidator : AbstractValidator<ApplyMemberCreditCommand>
{
    public ApplyMemberCreditCommandValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.SiteId).NotEmpty();
        RuleFor(x => x.MemberId).NotEmpty();
    }
}

public class ApplyMemberCreditCommandHandler : IRequestHandler<ApplyMemberCreditCommand, ApplyCreditResult>
{
    private readonly IChargeRepository _chargeRepo;
    private readonly IMemberCreditRepository _creditRepo;
    private readonly IArrearsRelief _arrearsRelief;

    public ApplyMemberCreditCommandHandler(
        IChargeRepository chargeRepo, IMemberCreditRepository creditRepo, IArrearsRelief arrearsRelief)
    {
        _chargeRepo = chargeRepo;
        _creditRepo = creditRepo;
        _arrearsRelief = arrearsRelief;
    }

    public async Task<ApplyCreditResult> Handle(ApplyMemberCreditCommand cmd, CancellationToken ct)
    {
        var balance = await _creditRepo.GetBalanceAsync(cmd.MemberId, ct);
        if (balance <= 0) return new ApplyCreditResult(0m, 0m);

        var charges = await ChargeSelection.LoadAsync(
            _chargeRepo, cmd.CompanyId, cmd.MemberId, cmd.ChargeIds, ct);

        var remaining = balance;
        foreach (var charge in charges.Where(IsCollectable).OrderBy(c => c.DueDate))
        {
            if (remaining <= 0) break;

            var toApply = Math.Min(remaining, charge.AmountOutstanding);
            if (toApply <= 0) continue;

            charge.ApplyPayment(toApply);
            _chargeRepo.Update(charge);
            await _creditRepo.AddAsync(
                MemberCreditMovement.DebitForCharge(cmd.CompanyId, cmd.SiteId, cmd.MemberId, toApply, charge.Id), ct);
            remaining -= toApply;
        }

        var applied = balance - remaining;

        if (applied > 0)
            await SaveAppliedAsync(cmd, ct);

        return new ApplyCreditResult(applied, remaining);
    }

    /// <summary>Cargos, movimientos del ledger y estado del socio comparten DbContext: una sola transacción.</summary>
    private async Task SaveAppliedAsync(ApplyMemberCreditCommand cmd, CancellationToken ct)
    {
        await _arrearsRelief.ClearIfSettledAsync(cmd.CompanyId, cmd.MemberId, ct);
        await _chargeRepo.SaveChangesAsync(ct);
    }

    /// <summary>Un cargo dado de baja o reembolsado no se revive aplicándole saldo a favor.</summary>
    private static bool IsCollectable(Charge charge) =>
        charge.Status is not (ChargeStatus.Paid or ChargeStatus.WrittenOff or ChargeStatus.Refunded);
}
