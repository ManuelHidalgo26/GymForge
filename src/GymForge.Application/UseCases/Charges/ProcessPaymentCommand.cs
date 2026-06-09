using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Charges;

// ── Create Charge ─────────────────────────────────────────────────────────────

public record CreateChargeCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid MemberId,
    Guid? MembershipId,
    ConceptType ConceptType,
    string Description,
    decimal Amount,
    DateOnly DueDate,
    decimal TaxAmount = 0m) : IRequest<ChargeDto>;

public class CreateChargeCommandHandler : IRequestHandler<CreateChargeCommand, ChargeDto>
{
    private readonly IChargeRepository _repo;
    public CreateChargeCommandHandler(IChargeRepository repo) => _repo = repo;

    public async Task<ChargeDto> Handle(CreateChargeCommand cmd, CancellationToken ct)
    {
        var charge = Charge.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId,
            cmd.MembershipId, cmd.ConceptType,
            cmd.Description, cmd.Amount, cmd.DueDate, cmd.TaxAmount);

        await _repo.AddAsync(charge, ct);
        await _repo.SaveChangesAsync(ct);
        return ChargeDto.FromEntity(charge);
    }
}

// ── Process Payment (N:M allocation) ─────────────────────────────────────────

public record ProcessPaymentCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid MemberId,
    Guid CashierId,
    decimal Amount,
    PaymentMethod Method,
    Guid? ShiftId,
    /// <summary>Specific charge IDs to allocate to. If empty, auto-allocates oldest first.</summary>
    IReadOnlyList<Guid>? ChargeIds = null,
    string? CardLast4 = null,
    string? CardBrand = null,
    string? ProcessorTxId = null) : IRequest<PaymentDto>;

public class ProcessPaymentCommandValidator : AbstractValidator<ProcessPaymentCommand>
{
    public ProcessPaymentCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("El monto debe ser positivo.");
        RuleFor(x => x.CardLast4)
            .NotEmpty().Length(4).When(x => x.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
            .WithMessage("Se requieren los últimos 4 dígitos de la tarjeta.");
    }
}

public class ProcessPaymentCommandHandler : IRequestHandler<ProcessPaymentCommand, PaymentDto>
{
    private readonly IChargeRepository _chargeRepo;

    public ProcessPaymentCommandHandler(IChargeRepository chargeRepo) => _chargeRepo = chargeRepo;

    public async Task<PaymentDto> Handle(ProcessPaymentCommand cmd, CancellationToken ct)
    {
        var payment = Payment.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId,
            cmd.CashierId, cmd.Amount, cmd.Method,
            cmd.ShiftId, cmd.CardLast4, cmd.CardBrand, cmd.ProcessorTxId);

        // Get pending charges to allocate to
        var pendingCharges = cmd.ChargeIds?.Any() == true
            ? (await Task.WhenAll(cmd.ChargeIds.Select(id => _chargeRepo.GetByIdAsync(id, ct))))
                .Where(c => c is not null).Cast<Charge>().ToList()
            : (await _chargeRepo.GetPendingAsync(cmd.MemberId, ct)).ToList();

        // Allocate payment oldest-first
        decimal remaining = cmd.Amount;
        foreach (var charge in pendingCharges.OrderBy(c => c.DueDate))
        {
            if (remaining <= 0) break;

            decimal toAllocate = Math.Min(remaining, charge.AmountOutstanding);
            if (toAllocate <= 0) continue;

            var alloc = PaymentAllocation.Create(payment.Id, charge.Id, toAllocate);
            payment.Allocations.Add(alloc);
            charge.ApplyPayment(toAllocate);
            _chargeRepo.Update(charge);
            remaining -= toAllocate;
        }

        await _chargeRepo.SaveChangesAsync(ct);
        return PaymentDto.FromEntity(payment);
    }
}
