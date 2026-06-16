using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Sales;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class SellProductCommandTests
{
    private readonly IProductRepository _productRepo = Substitute.For<IProductRepository>();
    private readonly ISaleRepository _saleRepo = Substitute.For<ISaleRepository>();
    private readonly IPaymentRepository _paymentRepo = Substitute.For<IPaymentRepository>();
    private readonly ICashRegister _cashRegister = Substitute.For<ICashRegister>();

    private SellProductCommandHandler Sut() => new(_productRepo, _saleRepo, _paymentRepo, _cashRegister);

    [Fact]
    public async Task Handle_CreatesSale_DiscountsStock_PostsCash()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var product = Product.Create(companyId, "AGUA-500", "Agua 500ml", 1_500m);
        var stock = StockBySite.Create(companyId, product.Id, siteId, 10);

        _productRepo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _productRepo.GetStockAsync(product.Id, siteId, Arg.Any<CancellationToken>()).Returns(stock);

        var dto = await Sut().Handle(new SellProductCommand(
            companyId, siteId, Guid.NewGuid(), Guid.NewGuid(),
            product.Id, Quantity: 2, MemberId: Guid.NewGuid(), PaymentMethod.Cash), CancellationToken.None);

        // 1500 * 2 = 3000 + IVA 21% (630) = 3630
        dto.Amount.Should().Be(3_630m);
        stock.Qty.Should().Be(8);

        await _saleRepo.Received(1).AddAsync(Arg.Any<Sale>(), Arg.Any<CancellationToken>());
        _saleRepo.Received(1).UpdateStock(stock);
        await _paymentRepo.Received(1).AddAsync(Arg.Any<Payment>(), Arg.Any<CancellationToken>());
        await _cashRegister.Received(1).PostIfCashAsync(
            PaymentMethod.Cash, Arg.Any<Guid?>(), CashMovementCategory.Sale, 3_630m,
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_VentaANoSocio_CreaPagoYVentaSinSocioConSaleId()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var product = Product.Create(companyId, "AGUA-500", "Agua 500ml", 1_500m);

        _productRepo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _productRepo.GetStockAsync(product.Id, siteId, Arg.Any<CancellationToken>())
            .Returns((StockBySite?)null);

        var dto = await Sut().Handle(new SellProductCommand(
            companyId, siteId, Guid.NewGuid(), null,
            product.Id, Quantity: 1, MemberId: null, PaymentMethod.Cash), CancellationToken.None);

        dto.MemberId.Should().BeNull();
        await _saleRepo.Received(1).AddAsync(
            Arg.Is<Sale>(s => s.MemberId == null), Arg.Any<CancellationToken>());
        await _paymentRepo.Received(1).AddAsync(
            Arg.Is<Payment>(p => p.MemberId == null && p.SaleId != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_NonPositiveQuantity_Fails() =>
        new SellProductCommandValidator().Validate(new SellProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), 0, Guid.NewGuid(), PaymentMethod.Cash))
            .IsValid.Should().BeFalse();

    [Fact]
    public void Validator_SinSocio_EsValido() =>
        new SellProductCommandValidator().Validate(new SellProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), 1, MemberId: null, PaymentMethod.Cash))
            .IsValid.Should().BeTrue();
}
