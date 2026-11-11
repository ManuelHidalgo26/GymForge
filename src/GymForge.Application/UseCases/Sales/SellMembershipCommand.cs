using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Domain.ValueObjects;
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
    private readonly IMemberRepository _memberRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly ICashRegister _cashRegister;
    private readonly IStaffRepository _staffRepo;
    private readonly ICompanyCalendar _calendar;
    private readonly IArrearsRelief _arrearsRelief;

    public SellMembershipCommandHandler(
        IMembershipTypeRepository planRepo,
        IChargeRepository chargeRepo,
        IMembershipRepository membershipRepo,
        IMemberRepository memberRepo,
        IPaymentRepository paymentRepo,
        ICashRegister cashRegister,
        IStaffRepository staffRepo,
        ICompanyCalendar calendar,
        IArrearsRelief arrearsRelief)
    {
        _arrearsRelief = arrearsRelief;
        _planRepo = planRepo;
        _chargeRepo = chargeRepo;
        _membershipRepo = membershipRepo;
        _memberRepo = memberRepo;
        _paymentRepo = paymentRepo;
        _cashRegister = cashRegister;
        _staffRepo = staffRepo;
        _calendar = calendar;
    }

    public async Task<PaymentDto> Handle(SellMembershipCommand cmd, CancellationToken ct)
    {
        var plan = await _planRepo.GetByIdAsync(cmd.MembershipTypeId, ct)
            ?? throw new InvalidOperationException("El plan seleccionado no existe.");

        var member = await _memberRepo.GetByIdAsync(cmd.MemberId, ct)
            ?? throw new InvalidOperationException("El socio seleccionado no existe.");

        if (plan.Price <= 0)
            throw new InvalidOperationException("El plan seleccionado no tiene costo; no requiere cobro.");

        var today = await _calendar.TodayAsync(cmd.CompanyId, ct);

        // Membresía activa según la duración del plan. Va primero: la cuota se vincula a ella para que los
        // reportes atribuyan lo cobrado al plan.
        var membership = Membership.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, plan.Id,
            today, plan.CalculateEndDate(today),
            visitsRemaining: plan.InitialVisits(), soldByStaffId: cmd.CashierId);
        membership.Activate();
        await FreezeCommissionAsync(membership, cmd, plan.Price, ct);

        // Cuota + pago que la salda.
        var charge = Charge.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, membership.Id,
            ConceptType.MembershipFee, $"Cuota {plan.Name}", plan.Price, today);
        charge.ApplyPayment(plan.Price);

        var payment = Payment.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, cmd.CashierId,
            plan.Price, cmd.Method, cmd.ShiftId, cmd.CardLast4);
        payment.Allocations.Add(PaymentAllocation.Create(payment.Id, charge.Id, plan.Price));

        await UpdateMemberStatusAsync(member, today, ct);

        await _membershipRepo.AddAsync(membership, ct);
        await _chargeRepo.AddAsync(charge, ct);
        await _paymentRepo.AddAsync(payment, ct);
        await _cashRegister.PostIfCashAsync(
            cmd.Method, cmd.ShiftId, CashMovementCategory.Membership, plan.Price, payment.Id, ct);

        await _chargeRepo.SaveChangesAsync(ct);
        return PaymentDto.FromEntity(payment);
    }

    /// <summary>
    /// Estado del socio tras la venta, según de dónde venga:
    /// Prospect / Expired / Cancelled pasan a Active (alta o reactivación, con evento de alta);
    /// Overdue solo sale de mora si no le quedan otras deudas vencidas (mismo mecanismo que el cobro);
    /// Active no cambia; Frozen y Suspended NO se tocan: congelar y suspender son decisiones explícitas
    /// del gimnasio y se levantan con descongelar / reactivar, no como efecto colateral de una venta.
    /// </summary>
    private async Task UpdateMemberStatusAsync(Member member, DateOnly today, CancellationToken ct)
    {
        switch (member.Status)
        {
            case MemberStatus.Prospect or MemberStatus.Expired or MemberStatus.Cancelled:
                member.Activate(today);
                _memberRepo.Update(member);
                break;
            case MemberStatus.Overdue:
                await _arrearsRelief.ClearIfSettledAsync(member.CompanyId, member.Id, ct);
                break;
        }
    }

    /// <summary>
    /// Congela la comisión del cajero que vende: % de membresías que tiene hoy sobre el precio del plan. Un cajero
    /// inexistente o de otra empresa no gana comisión, pero la venta sigue.
    /// </summary>
    private async Task FreezeCommissionAsync(Membership membership, SellMembershipCommand cmd, decimal price, CancellationToken ct)
    {
        var seller = await SellerLookup.FindAsync(_staffRepo, cmd.CompanyId, cmd.CashierId, ct);
        var pct = seller?.CommissionPctMemberships ?? 0m;
        membership.SetCommission(cmd.CashierId, Commission.Calculate(price, pct));
    }
}
