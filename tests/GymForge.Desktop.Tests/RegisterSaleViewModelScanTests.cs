using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Catalog;
using GymForge.Application.UseCases.Members;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Caja (POS): un código escaneado elige primero un producto por código de barras y, si no, un socio.</summary>
public class RegisterSaleViewModelScanTests
{
    private static readonly ProductDto Agua = new(Guid.NewGuid(), "AGUA-500", "Agua 500ml", 1_500m);
    private static readonly ProductDto Barra = new(Guid.NewGuid(), "BARRA", "Barra de cereal", 900m);

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private async Task<RegisterSaleViewModel> SutAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var session = await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
        var receipts = new ReceiptService(Substitute.For<IServiceScopeFactory>());

        return new RegisterSaleViewModel(_mediator, Substitute.For<IMemberRepository>(), session, receipts)
        {
            Products = [Agua, Barra],
        };
    }

    private static MemberDto NewMemberDto(string firstName = "Ana") => MemberDto.FromEntity(
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), firstName, "García", DocumentType.DNI, "30123456", Gender.Female));

    private void BarcodeBelongsTo(string code, ProductDto product) =>
        _mediator.Send(Arg.Is<FindProductByBarcodeQuery>(q => q.Barcode == code), Arg.Any<CancellationToken>())
            .Returns(product);

    private void CredentialBelongsTo(string code, MemberDto member) =>
        _mediator.Send(Arg.Is<FindMemberByCredentialQuery>(q => q.Code == code), Arg.Any<CancellationToken>())
            .Returns(member);

    // ── Producto ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scan_ProductBarcode_SwitchesToProductAndSelectsItFromTheLoadedList()
    {
        var vm = await SutAsync();
        BarcodeBelongsTo("7791234567890", Agua with { });   // otra instancia, mismo producto

        await vm.ScanAsync("7791234567890");

        vm.Concept.Should().Be(SaleConcept.Product);
        vm.SelectedProduct.Should().BeSameAs(vm.Products[0]);
        vm.Quantity.Should().Be(1);
        vm.ScanMessage.Should().Contain("Agua 500ml");
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Scan_TheSelectedProductAgain_AddsOneUnit()
    {
        var vm = await SutAsync();
        BarcodeBelongsTo("7791234567890", Agua);

        await vm.ScanAsync("7791234567890");
        await vm.ScanAsync("7791234567890");
        await vm.ScanAsync("7791234567890");

        vm.SelectedProduct!.Id.Should().Be(Agua.Id);
        vm.Quantity.Should().Be(3);
    }

    [Fact]
    public async Task Scan_ADifferentProduct_StartsAtOneUnit()
    {
        var vm = await SutAsync();
        BarcodeBelongsTo("1111", Agua);
        BarcodeBelongsTo("2222", Barra);
        await vm.ScanAsync("1111");
        await vm.ScanAsync("1111");

        await vm.ScanAsync("2222");

        vm.SelectedProduct!.Id.Should().Be(Barra.Id);
        vm.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Scan_AProductMissingFromTheLoadedList_IsAddedToIt()
    {
        var vm = await SutAsync();
        var nuevo = new ProductDto(Guid.NewGuid(), "NUEVO", "Producto nuevo", 2_000m);
        BarcodeBelongsTo("3333", nuevo);

        await vm.ScanAsync("3333");

        vm.Products.Should().Contain(nuevo);
        vm.SelectedProduct.Should().Be(nuevo);
    }

    [Fact]
    public async Task Scan_ProductWinsOverMember_AndTheMemberIsNeverLookedUp()
    {
        var vm = await SutAsync();
        BarcodeBelongsTo("30123456", Agua);
        CredentialBelongsTo("30123456", NewMemberDto());

        await vm.ScanAsync("30123456");

        vm.SelectedProduct!.Id.Should().Be(Agua.Id);
        vm.SelectedMember.Should().BeNull();
        await _mediator.DidNotReceive().Send(Arg.Any<FindMemberByCredentialQuery>(), Arg.Any<CancellationToken>());
    }

    // ── Socio ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Scan_MemberCredential_SelectsTheMember_AndDropsTheWalkInMode()
    {
        var vm = await SutAsync();
        var ana = NewMemberDto();
        vm.Members = [ana];
        vm.IsProduct = true;
        vm.IsWalkIn = true;
        CredentialBelongsTo("CARNET-77", ana with { });

        await vm.ScanAsync("CARNET-77");

        vm.IsWalkIn.Should().BeFalse();
        vm.SelectedMember.Should().BeSameAs(vm.Members[0]);
        vm.ScanMessage.Should().Contain("Ana García");
        vm.Concept.Should().Be(SaleConcept.Product, "escanear un socio no cambia el concepto");
    }

    [Fact]
    public async Task Scan_AMemberBeyondTheLoadedPage_IsAddedToTheList()
    {
        var vm = await SutAsync();
        var lejano = NewMemberDto("Zacarías");
        CredentialBelongsTo("CARNET-99", lejano);

        await vm.ScanAsync("CARNET-99");

        vm.Members.Should().Contain(lejano);
        vm.SelectedMember.Should().Be(lejano);
    }

    // ── Sin resolver / robustez ───────────────────────────────────────────────

    [Fact]
    public async Task Scan_UnknownCode_ReportsItAndKeepsTheCurrentSelection()
    {
        var vm = await SutAsync();
        vm.SelectedProduct = vm.Products[1];

        await vm.ScanAsync("9999");

        vm.ErrorMessage.Should().Contain("9999");
        vm.ScanMessage.Should().BeNull();
        vm.SelectedProduct.Should().Be(Barra);
    }

    [Fact]
    public async Task Scan_ASuccessfulScan_ClearsThePreviousError()
    {
        var vm = await SutAsync();
        BarcodeBelongsTo("1111", Agua);
        await vm.ScanAsync("9999");

        await vm.ScanAsync("1111");

        vm.ErrorMessage.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Scan_BlankCode_IsIgnored(string code)
    {
        var vm = await SutAsync();

        await vm.ScanAsync(code);

        vm.ErrorMessage.Should().BeNull();
        _mediator.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task Scan_WhileSaving_IsIgnored()
    {
        var vm = await SutAsync();
        vm.IsSaving = true;
        BarcodeBelongsTo("1111", Agua);

        await vm.ScanAsync("1111");

        vm.SelectedProduct.Should().BeNull();
    }

    [Fact]
    public async Task Scan_WhenTheLookupFails_ReportsTheErrorWithoutThrowing()
    {
        var vm = await SutAsync();
        _mediator.Send(Arg.Any<FindProductByBarcodeQuery>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("base de datos no disponible"));

        var act = () => vm.ScanAsync("1111");

        await act.Should().NotThrowAsync();
        vm.ErrorMessage.Should().Be("base de datos no disponible");
    }
}
