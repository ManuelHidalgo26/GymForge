using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Products;
using GymForge.Domain.Entities;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ProductCommandsTests
{
    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _siteId = Guid.NewGuid();

    private Product MakeProduct(string sku = "PROT-1KG", string name = "Proteína 1kg")
    {
        var product = Product.Create(_companyId, sku, name, 25_000m, 18_000m);
        _repo.GetByIdAsync(product.Id, Arg.Any<CancellationToken>()).Returns(product);
        return product;
    }

    // ── Crear ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateProduct_PersistsAndReturnsRow()
    {
        var dto = await new CreateProductCommandHandler(_repo).Handle(
            new CreateProductCommand(_companyId, " PROT-1KG ", " Proteína 1kg ", 25_000m, 18_000m, null),
            CancellationToken.None);

        dto.Sku.Should().Be("PROT-1KG");
        dto.Name.Should().Be("Proteína 1kg");
        dto.StockQty.Should().Be(0);
        await _repo.Received(1).AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_Throws()
    {
        var existing = MakeProduct();
        _repo.GetBySkuAsync(_companyId, "PROT-1KG", Arg.Any<CancellationToken>())
            .Returns(existing);

        var act = () => new CreateProductCommandHandler(_repo).Handle(
            new CreateProductCommand(_companyId, "PROT-1KG", "Otra proteína", 30_000m, 0, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SKU*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Product>(), Arg.Any<CancellationToken>());
    }

    // ── Modificar / activar ─────────────────────────────────────────────────

    [Fact]
    public async Task UpdateProduct_ChangesFields()
    {
        var product = MakeProduct();

        var dto = await new UpdateProductCommandHandler(_repo).Handle(
            new UpdateProductCommand(_companyId, product.Id, "Proteína 2kg", 45_000m, 30_000m, "779123"),
            CancellationToken.None);

        dto.Name.Should().Be("Proteína 2kg");
        dto.SalePrice.Should().Be(45_000m);
        dto.Barcode.Should().Be("779123");
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateProduct_OtherTenant_Throws()
    {
        var product = MakeProduct();

        var act = () => new UpdateProductCommandHandler(_repo).Handle(
            new UpdateProductCommand(Guid.NewGuid(), product.Id, "X", 1m, 0, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetProductActive_Toggles()
    {
        var product = MakeProduct();

        await new SetProductActiveCommandHandler(_repo).Handle(
            new SetProductActiveCommand(_companyId, product.Id, false), CancellationToken.None);

        product.IsActive.Should().BeFalse();
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Stock ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdjustStock_CreatesStockRecordWhenMissing()
    {
        var product = MakeProduct();
        _repo.GetStockAsync(product.Id, _siteId, Arg.Any<CancellationToken>())
            .Returns((StockBySite?)null);

        var qty = await new AdjustStockCommandHandler(_repo).Handle(
            new AdjustStockCommand(_companyId, _siteId, product.Id, 12m, ReorderPoint: 3m),
            CancellationToken.None);

        qty.Should().Be(12m);
        await _repo.Received(1).AddStockAsync(
            Arg.Is<StockBySite>(s => s.SiteId == _siteId && s.CompanyId == _companyId),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AdjustStock_NegativeBelowZero_Throws()
    {
        var product = MakeProduct();
        var stock = StockBySite.Create(_companyId, product.Id, _siteId, qty: 5m);
        _repo.GetStockAsync(product.Id, _siteId, Arg.Any<CancellationToken>()).Returns(stock);

        var act = () => new AdjustStockCommandHandler(_repo).Handle(
            new AdjustStockCommand(_companyId, _siteId, product.Id, -8m),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        stock.Qty.Should().Be(5m);
    }

    [Fact]
    public async Task AdjustStock_UpdatesExistingQtyAndReorder()
    {
        var product = MakeProduct();
        var stock = StockBySite.Create(_companyId, product.Id, _siteId, qty: 5m);
        _repo.GetStockAsync(product.Id, _siteId, Arg.Any<CancellationToken>()).Returns(stock);

        var qty = await new AdjustStockCommandHandler(_repo).Handle(
            new AdjustStockCommand(_companyId, _siteId, product.Id, -2m, ReorderPoint: 4m),
            CancellationToken.None);

        qty.Should().Be(3m);
        stock.ReorderPoint.Should().Be(4m);
        await _repo.DidNotReceive().AddStockAsync(Arg.Any<StockBySite>(), Arg.Any<CancellationToken>());
    }
}
