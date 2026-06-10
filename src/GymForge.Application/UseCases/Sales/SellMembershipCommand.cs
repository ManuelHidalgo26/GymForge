using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Sales;

/// <summary>
/// Cobra una membresía a un socio: genera la cuota, la salda con un pago,
/// crea la membresía activa e impacta la caja (si es efectivo).
/// </summary>
public record SellMembershipCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid CashierId,
    Guid? ShiftId,
    Guid MemberId,
    Guid MembershipTypeId,
    PaymentMethod Method,
    string? CardLast4 = null) : IRequest<PaymentDto>;

public class SellMembershipCommandValidator : AbstractValidator<SellMembershipCommand>
{
    public SellMembershipCommandValidator()
    {
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("Elegí un socio.");
        RuleFor(x => x.MembershipTypeId).NotEmpty().WithMessage("Elegí un plan.");
        RuleFor(x => x.CardLast4)
            .NotEmpty().Length(4).When(x => x.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
            .WithMessage("Se requieren los últimos 4 dígitos de la tarjeta.");
    }
}

public class SellMembershipCommandHandler : IRequestHandler<SellMembershipCommand, PaymentDto>
{
    private readonly IMembershipTypeRepository _planRepo;
    private readonly IChargeRepository _chargeRepo;
    private readonly IMembershipRepository _membershipRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly ICashRegister _cashRegister;

    public SellMembershipCommandHandler(
        IMembershipTypeRepository planRepo,
        IChargeRepository chargeRepo,
        IMembershipRepository membershipRepo,
        IPaymentRepository paymentRepo,
        ICashRegister cashRegister)
    {
        _planRepo = planRepo;
        _chargeRepo = chargeRepo;
        _membershipRepo = membershipRepo;
        _paymentRepo = paymentRepo;
        _cashRegister = cashRegister;
    }

    public async Task<PaymentDto> Handle(SellMembershipCommand cmd, CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(cmd.MembershipTypeId, ct)
            ?? throw new InvalidOperationException("El plan seleccionado no existe.");

        if (plan.Price <= 0)
            throw new InvalidOperationException("El plan seleccionado no tiene costo; no requiere cobro.");

        var today = DateOnly.FromDateTime(DateTime.Today);

        // Cuota + pago que la salda.
        var charge = Charge.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, null,
            ConceptType.MembershipFee, $"Cuota {plan.Name}", plan.Price, today);
        charge.ApplyPayment(plan.Price);

        var payment = Payment.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, cmd.CashierId,
            plan.Price, cmd.Method, cmd.ShiftId, cmd.CardLast4);
        payment.Allocations.Add(PaymentAllocation.Create(payment.Id, charge.Id, plan.Price));

        // Membresía activa según la duración del plan.
        var membership = Membership.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, plan.Id,
            today, CalculateEnd(today, plan), soldByStaffId: cmd.CashierId);
        membership.Activate();

        await _chargeRepo.AddAsync(charge, ct);
        await _membershipRepo.AddAsync(membership, ct);
        await _paymentRepo.AddAsync(payment, ct);
        await _cashRegister.PostIfCashAsync(
            cmd.Method, cmd.ShiftId, CashMovementCategory.Membership, plan.Price, payment.Id, ct);

        await _chargeRepo.SaveChangesAsync(ct);
        return PaymentDto.FromEntity(payment);
    }

    private static DateOnly? CalculateEnd(DateOnly start, MembershipType plan) => plan.DurationUnit switch
    {
        "Day" => start.AddDays(plan.DurationValue),
        "Year" => start.AddYears(plan.DurationValue),
        _ => start.AddMonths(plan.DurationValue),
    };
}
