using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Staff;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Login de la caja por PIN: reintento cómodo y aviso del PIN de fábrica solo mientras siga activo.</summary>
public class CashViewModelLoginTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");

    private async Task<CashViewModel> SutAsync()
    {
        var site = Site.Create(_company.Id, "Central", "Calle 1");
        var session = await TestSession.InitializedAsync(_company, [site]);
        return new CashViewModel(
            _mediator, Substitute.For<IShiftRepository>(), Substitute.For<IMemberRepository>(), session,
            new ReceiptService(Substitute.For<IServiceScopeFactory>()));
    }

    private void PinIsDefault(bool inUse) =>
        _mediator.Send(Arg.Any<CheckDefaultPinQuery>(), Arg.Any<CancellationToken>()).Returns(inUse);

    [Fact]
    public async Task Login_ConPinIncorrecto_LimpiaElPinYAvisa()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>(), Arg.Any<CancellationToken>()).Returns((StaffDto?)null);
        sut.Pin = "9999";

        await sut.LoginCommand.ExecuteAsync(null);

        sut.Pin.Should().BeEmpty();
        sut.StatusMessage.Should().Be("PIN incorrecto.");
        sut.Session.IsSignedIn.Should().BeFalse();
    }

    [Fact]
    public async Task Login_ConPinCorrecto_IngresaYLimpiaElPin()
    {
        var sut = await SutAsync();
        _mediator.Send(Arg.Any<AuthenticateStaffCommand>(), Arg.Any<CancellationToken>())
            .Returns(new StaffDto(Guid.NewGuid(), "Admin", StaffRole.Admin, "#000000"));
        sut.Pin = "1234";

        await sut.LoginCommand.ExecuteAsync(null);

        sut.Pin.Should().BeEmpty();
        sut.Session.IsSignedIn.Should().BeTrue();
    }

    [Fact]
    public async Task Load_ConPinDeFabricaActivo_MuestraElAvisoDelAdminDeEjemplo()
    {
        PinIsDefault(true);
        var sut = await SutAsync();

        await sut.LoadAsync();

        sut.IsDefaultPinInUse.Should().BeTrue();
    }

    [Fact]
    public async Task Load_ConPinPropio_OcultaElAvisoDelAdminDeEjemplo()
    {
        PinIsDefault(false);
        var sut = await SutAsync();

        await sut.LoadAsync();

        sut.IsDefaultPinInUse.Should().BeFalse();
    }

    [Fact]
    public async Task SignOut_VuelveAConsultarElPinDeFabrica_PorqueSePudoCambiarEnConfig()
    {
        PinIsDefault(true);
        var sut = await SutAsync();
        await sut.LoadAsync();
        PinIsDefault(false);

        await sut.SignOutCommand.ExecuteAsync(null);

        sut.IsDefaultPinInUse.Should().BeFalse();
    }
}
