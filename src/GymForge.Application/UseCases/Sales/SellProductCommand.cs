using FluentValidation;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;

namespace GymForge.Application.UseCases.Sales;

/// <summary>
/// Vende un producto: genera la venta, descuenta stock de la sede, registra el
/// pago e impacta la caja (si es efectivo). El socio es opcional (venta a no socio).
/// </summary>
public record SellProductCommand(
    Guid CompanyId,
    Guid SiteId,
    Guid CashierId,
    Guid? ShiftId,
    Guid ProductId,
    decimal Quantity,
    Guid MemberId,
    PaymentMethod Method,
    string? CardLast4 = null) : IRequest<PaymentDto>;

public class SellProductCommandValidator : AbstractValidator<SellProductCommand>
{
    public SellProductCommandValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage("Elegí un producto.");
        RuleFor(x => x.MemberId).NotEmpty().WithMessage("Elegí el socio al que se le vende.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("La cantidad debe ser positiva.");
        RuleFor(x => x.CardLast4)
            .NotEmpty().Length(4).When(x => x.Method is PaymentMethod.CreditCard or PaymentMethod.DebitCard)
            .WithMessage("Se requieren los últimos 4 dígitos de la tarjeta.");
    }
}

public class SellProductCommandHandler : IRequestHandler<SellProductCommand, PaymentDto>
{
    private readonly IProductRepository _productRepo;
    private readonly ISaleRepository _saleRepo;
    private readonly IPaymentRepository _paymentRepo;
    private readonly ICashRegister _cashRegister;

    public SellProductCommandHandler(
        IProductRepository productRepo,
        ISaleRepository saleRepo,
        IPaymentRepository paymentRepo,
        ICashRegister cashRegister)
    {
        _productRepo = productRepo;
        _saleRepo = saleRepo;
        _paymentRepo = paymentRepo;
        _cashRegister = cashRegister;
    }

    public async Task<PaymentDto> Handle(SellProductCommand cmd, CancellationToken ct)
    {
        var product = await _productRepo.GetByIdAsync(cmd.ProductId, ct)
            ?? throw new InvalidOperationException("El producto seleccionado no existe.");

        var sale = Sale.Create(cmd.CompanyId, cmd.SiteId, cmd.CashierId, cmd.MemberId, cmd.ShiftId);
        sale.Lines.Add(SaleLine.Create(sale.Id, product.Name, cmd.Quantity, product.SalePrice, product.TaxRate));
        sale.RecalculateTotals();

        // Descontar stock de la sede (si está cargado). Lanza si quedaría negativo.
        var stock = await _productRepo.GetStockAsync(cmd.ProductId, cmd.SiteId, ct);
        if (stock is not null)
        {
            stock.AdjustStock(-cmd.Quantity);
            _saleRepo.UpdateStock(stock);
        }

        var payment = Payment.Create(
            cmd.CompanyId, cmd.SiteId, cmd.MemberId, cmd.CashierId,
            sale.Total, cmd.Method, cmd.ShiftId, cmd.CardLast4);

        await _saleRepo.AddAsync(sale, ct);
        await _paymentRepo.AddAsync(payment, ct);
        await _cashRegister.PostIfCashAsync(
            cmd.Method, cmd.ShiftId, CashMovementCategory.Sale, sale.Total, payment.Id, ct);

        await _saleRepo.SaveChangesAsync(ct);
        return PaymentDto.FromEntity(payment);
    }
}
