using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Products;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.ViewModels.Products;
using GymForge.Desktop.ViewModels.Settings;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Tarifas de comisión editables: card de personal en Configuración y campo % en el ABM de Productos.</summary>
public class CommissionTariffsViewModelTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Company _company = Company.Create("Gym Test", "30-12345678-9");

    private async Task<(StaffCommissionsViewModel Vm, Guid CompanyId)> StaffSutAsync()
    {
        var session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        return (new StaffCommissionsViewModel(_mediator, session), _company.Id);
    }

    private static StaffCommissionRatesDto Rates(string name, decimal memberships, decimal products) =>
        new(Guid.NewGuid(), name, StaffRole.Receptionist, memberships, products);

    // ── Personal ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Staff_Load_ListsTheStaffOfTheSessionCompanyWithTheirRates()
    {
        var (vm, companyId) = await StaffSutAsync();
        var ana = Rates("Ana Paz", 10m, 5m);
        _mediator.Send(Arg.Is<GetStaffCommissionRatesQuery>(q => q.CompanyId == companyId), Arg.Any<CancellationToken>())
            .Returns(new List<StaffCommissionRatesDto> { ana });

        await vm.LoadAsync();

        var row = vm.Staff.Should().ContainSingle().Subject;
        row.StaffId.Should().Be(ana.StaffId);
        row.PctMemberships.Should().Be(10m);
        row.PctProducts.Should().Be(5m);
    }

    [Fact]
    public async Task Staff_Save_SendsThePercentagesOfThatRow_AndReportsSuccess()
    {
        var (vm, companyId) = await StaffSutAsync();
        var ana = Rates("Ana Paz", 0m, 0m);
        _mediator.Send(Arg.Any<GetStaffCommissionRatesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<StaffCommissionRatesDto> { ana });
        await vm.LoadAsync();
        var row = vm.Staff.Single();
        row.PctMemberships = 12.5m;
        row.PctProducts = 4m;
        _mediator.Send(Arg.Any<UpdateStaffCommissionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(ana with { PctMemberships = 12.5m, PctProducts = 4m });

        await vm.SaveCommand.ExecuteAsync(row);

        await _mediator.Received(1).Send(
            Arg.Is<UpdateStaffCommissionsCommand>(c =>
                c.CompanyId == companyId && c.StaffId == ana.StaffId && c.PctMemberships == 12.5m && c.PctProducts == 4m),
            Arg.Any<CancellationToken>());
        row.Saved.Should().BeTrue();
        row.Message.Should().Be("Guardado.");
    }

    [Fact]
    public async Task Staff_Save_WhenValidationFails_ShowsTheMessageOnThatRow()
    {
        var (vm, _) = await StaffSutAsync();
        _mediator.Send(Arg.Any<GetStaffCommissionRatesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<StaffCommissionRatesDto> { Rates("Ana Paz", 0m, 0m) });
        await vm.LoadAsync();
        var row = vm.Staff.Single();
        _mediator.Send(Arg.Any<UpdateStaffCommissionsCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ValidationException([new ValidationFailure("PctMemberships", "La comisión es un porcentaje de 0 a 100.")]));

        await vm.SaveCommand.ExecuteAsync(row);

        row.Saved.Should().BeFalse();
        row.Message.Should().Be("La comisión es un porcentaje de 0 a 100.");
    }

    [Fact]
    public async Task Staff_Save_EmptyFieldsAreSentAsZero()
    {
        var (vm, _) = await StaffSutAsync();
        var ana = Rates("Ana Paz", 10m, 10m);
        _mediator.Send(Arg.Any<GetStaffCommissionRatesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<StaffCommissionRatesDto> { ana });
        await vm.LoadAsync();
        var row = vm.Staff.Single();
        row.PctMemberships = null;
        row.PctProducts = null;
        _mediator.Send(Arg.Any<UpdateStaffCommissionsCommand>(), Arg.Any<CancellationToken>())
            .Returns(ana with { PctMemberships = 0m, PctProducts = 0m });

        await vm.SaveCommand.ExecuteAsync(row);

        await _mediator.Received(1).Send(
            Arg.Is<UpdateStaffCommissionsCommand>(c => c.PctMemberships == 0m && c.PctProducts == 0m),
            Arg.Any<CancellationToken>());
    }

    // ── Productos ─────────────────────────────────────────────────────────────

    private async Task<ProductsViewModel> ProductsSutAsync()
    {
        var session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        _mediator.Send(Arg.Any<GetProductsWithStockQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProductRowDto>());
        return new ProductsViewModel(_mediator, session);
    }

    [Fact]
    public async Task Products_Edit_LoadsThePercentage_AndSavingSendsIt()
    {
        var vm = await ProductsSutAsync();
        var row = new ProductRowDto(Guid.NewGuid(), "BARRA", "Barra", null, 1_000m, 500m, 3m, 0m, true, CommissionPct: 7.5m);

        vm.EditProductCommand.Execute(row);
        vm.CommissionPct.Should().Be(7.5m);
        vm.CommissionPct = 10m;
        await vm.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<UpdateProductCommand>(c => c.ProductId == row.Id && c.CommissionPct == 10m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Products_Create_SendsThePercentage()
    {
        var vm = await ProductsSutAsync();

        vm.OpenCreateFormCommand.Execute(null);
        vm.Sku = "BARRA";
        vm.Name = "Barra";
        vm.SalePrice = 1_000m;
        vm.CommissionPct = 5m;
        await vm.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<CreateProductCommand>(c => c.Sku == "BARRA" && c.CommissionPct == 5m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Products_OpenCreateForm_ClearsAPercentageLeftByAPreviousEdit()
    {
        var vm = await ProductsSutAsync();
        vm.EditProductCommand.Execute(
            new ProductRowDto(Guid.NewGuid(), "BARRA", "Barra", null, 1_000m, 500m, 3m, 0m, true, CommissionPct: 7.5m));

        vm.OpenCreateFormCommand.Execute(null);

        vm.CommissionPct.Should().BeNull();
    }
}
