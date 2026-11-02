using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Catalog;
using GymForge.Domain.Entities;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class FindProductByBarcodeQueryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly IProductRepository _repo = Substitute.For<IProductRepository>();

    private Task<ProductDto?> RunAsync(Guid companyId, string barcode) =>
        new FindProductByBarcodeQueryHandler(_repo)
            .Handle(new FindProductByBarcodeQuery(companyId, barcode), CancellationToken.None);

    [Fact]
    public async Task Handle_BarcodeOfAProduct_ReturnsIt()
    {
        var agua = Product.Create(CompanyId, "AGUA-500", "Agua 500ml", 1_500m);
        _repo.GetByBarcodeAsync(CompanyId, "7791234567890", Arg.Any<CancellationToken>()).Returns(agua);

        var dto = await RunAsync(CompanyId, "7791234567890");

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(agua.Id);
        dto.Name.Should().Be("Agua 500ml");
        dto.SalePrice.Should().Be(1_500m);
    }

    [Fact]
    public async Task Handle_LooksUpTheTrimmedBarcode()
    {
        await RunAsync(CompanyId, " 7791234567890 ");

        await _repo.Received(1).GetByBarcodeAsync(CompanyId, "7791234567890", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownBarcode_ReturnsNull()
    {
        _repo.GetByBarcodeAsync(CompanyId, "0000", Arg.Any<CancellationToken>()).ReturnsNull();

        (await RunAsync(CompanyId, "0000")).Should().BeNull();
    }

    [Fact]
    public async Task Handle_BlankBarcode_ReturnsNullWithoutQuerying()
    {
        (await RunAsync(CompanyId, "  ")).Should().BeNull();

        await _repo.DidNotReceive().GetByBarcodeAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AlwaysQueriesTheRequestedCompany()
    {
        var agua = Product.Create(CompanyId, "AGUA-500", "Agua 500ml", 1_500m);
        _repo.GetByBarcodeAsync(CompanyId, "7791234567890", Arg.Any<CancellationToken>()).Returns(agua);

        (await RunAsync(Guid.NewGuid(), "7791234567890")).Should().BeNull();
    }
}
