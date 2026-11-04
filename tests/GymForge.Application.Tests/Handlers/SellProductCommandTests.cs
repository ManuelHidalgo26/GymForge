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

    private readonly IStaffRepository _staffRepo = Substitute.For<IStaffRepository>();

    private SellProductCommandHandler Sut() => new(_productRepo, _saleRepo, _paymentRepo, _cashRegister, _staffRepo);

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

    // ── Comisión del vendedor (snapshot al vender) ────────────────────────────

    private async Task<(Sale Sold, Guid CashierId)> SellWithAsync(Product product, Staff? seller, decimal quantity = 2)
    {
        var siteId = Guid.NewGuid();
        _productRepo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        _productRepo.GetStockAsync(product.Id, siteId, Arg.Any<CancellationToken>()).Returns((StockBySite?)null);
        var cashierId = seller?.Id ?? Guid.NewGuid();
        if (seller is not null)
            _staffRepo.GetByIdAsync(seller.Id, Arg.Any<CancellationToken>()).Returns(seller);

        Sale? sold = null;
        await _saleRepo.AddAsync(Arg.Do<Sale>(s => sold = s), Arg.Any<CancellationToken>());

        await Sut().Handle(new SellProductCommand(
            product.CompanyId, siteId, cashierId, null, product.Id, quantity, null, PaymentMethod.Cash),
            CancellationToken.None);
        return (sold!, cashierId);
    }

    private static Product ProductWithPct(decimal price, decimal pct)
    {
        var product = Product.Create(Guid.NewGuid(), "BARRA", "Barra proteica", price);
        product.SetCommissionPct(pct);
        return product;
    }

    private static Staff SellerWithProductRate(Guid companyId, decimal pct)
    {
        var seller = Staff.Create(companyId, "Caro", "Vendedora", StaffRole.Receptionist, "hash");
        seller.SetCommissions(0m, 0m, pct);
        return seller;
    }

    [Fact]
    public async Task Handle_FreezesTheProductCommissionOverTheLineTotalWithoutVat()
    {
        var (sold, cashierId) = await SellWithAsync(ProductWithPct(price: 1_500m, pct: 10m), seller: null);

        var line = sold.Lines.Should().ContainSingle().Subject;
        line.CommissionAmount.Should().Be(300m, "10% de 3.000 (sin IVA)");
        line.CommissionStaffId.Should().Be(cashierId);
    }

    [Fact]
    public async Task Handle_RoundsTheCommissionToCents()
    {
        // 12.345 * 1 * 7,5% = 925,875 -> 925,88
        var (sold, _) = await SellWithAsync(ProductWithPct(price: 12_345m, pct: 7.5m), seller: null, quantity: 1);

        sold.Lines.Single().CommissionAmount.Should().Be(925.88m);
    }

    [Fact]
    public async Task Handle_ProductWithoutPercentage_FallsBackToTheSellersProductRate()
    {
        var product = Product.Create(Guid.NewGuid(), "BARRA", "Barra proteica", 1_000m);

        var (sold, _) = await SellWithAsync(product, SellerWithProductRate(product.CompanyId, 5m));

        sold.Lines.Single().CommissionAmount.Should().Be(100m, "5% de 2.000");
    }

    [Fact]
    public async Task Handle_ProductPercentageWinsOverTheSellersRate()
    {
        var product = ProductWithPct(price: 1_000m, pct: 10m);

        var (sold, _) = await SellWithAsync(product, SellerWithProductRate(product.CompanyId, 5m));

        sold.Lines.Single().CommissionAmount.Should().Be(200m);
    }

    [Fact]
    public async Task Handle_SellerOfAnotherCompany_DoesNotEarnTheFallbackRate()
    {
        var product = Product.Create(Guid.NewGuid(), "BARRA", "Barra proteica", 1_000m);

        var (sold, _) = await SellWithAsync(product, SellerWithProductRate(Guid.NewGuid(), 5m));

        sold.Lines.Single().CommissionAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_NoPercentageAnywhere_EarnsNothing()
    {
        var (sold, _) = await SellWithAsync(Product.Create(Guid.NewGuid(), "BARRA", "Barra", 1_000m), seller: null);

        sold.Lines.Single().CommissionAmount.Should().Be(0m);
    }
}
