using FluentAssertions;
using GymForge.Application.UseCases.Access;
using GymForge.Desktop.ViewModels.Checkin;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Un escaneo del lector entra al kiosk por el mismo Gatekeeper que la carga manual, como Barcode.</summary>
public class CheckInKioskViewModelScanTests
{
    private readonly IValidateSwipeUseCase _gatekeeper = Substitute.For<IValidateSwipeUseCase>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private static AccessDecision Granted() =>
        new(true, Guid.NewGuid(), "Ana García", null, null, false, 0m, MembershipStatus.Active);

    private async Task<(CheckInKioskViewModel Vm, Company Company, Site Site)> SutAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var site = Site.Create(company.Id, "Central", "Calle 1");
        var session = await TestSession.InitializedAsync(company, [site]);
        return (new CheckInKioskViewModel(_gatekeeper, _mediator, session), company, site);
    }

    [Fact]
    public async Task ProcessScan_SendsTheCodeToTheGatekeeperAsBarcode_ForTheSessionDoor()
    {
        var (vm, company, site) = await SutAsync();
        _gatekeeper.ValidateSwipeAsync(Arg.Any<ValidateSwipeRequest>(), Arg.Any<CancellationToken>()).Returns(Granted());

        await vm.ProcessScanAsync("CARNET-77");

        await _gatekeeper.Received(1).ValidateSwipeAsync(
            Arg.Is<ValidateSwipeRequest>(r =>
                r.Credential == "CARNET-77" &&
                r.Method == AccessMethod.Barcode &&
                r.CompanyId == company.Id &&
                r.SiteId == site.Id),
            Arg.Any<CancellationToken>());
        vm.State.Should().Be(KioskState.Granted);
        vm.MemberName.Should().Be("Ana García");
        vm.ResetCommand.Execute(null);   // corta el contador de cierre automático
    }

    [Fact]
    public async Task ProcessScan_ClearsTheCharacterThatLeakedIntoTheDniField()
    {
        var (vm, _, _) = await SutAsync();
        _gatekeeper.ValidateSwipeAsync(Arg.Any<ValidateSwipeRequest>(), Arg.Any<CancellationToken>()).Returns(Granted());
        vm.CredentialInput = "7";   // el primer carácter de la ráfaga pasa al campo de DNI

        await vm.ProcessScanAsync("7791234567890");

        vm.CredentialInput.Should().BeEmpty();
        vm.ResetCommand.Execute(null);
    }

    [Fact]
    public async Task ManualCheckIn_StillGoesThroughTheGatekeeperAsManual()
    {
        var (vm, _, _) = await SutAsync();
        _gatekeeper.ValidateSwipeAsync(Arg.Any<ValidateSwipeRequest>(), Arg.Any<CancellationToken>()).Returns(Granted());
        vm.CredentialInput = " 30111222 ";

        await vm.ManualCheckInCommand.ExecuteAsync(null);

        await _gatekeeper.Received(1).ValidateSwipeAsync(
            Arg.Is<ValidateSwipeRequest>(r => r.Credential == "30111222" && r.Method == AccessMethod.Manual),
            Arg.Any<CancellationToken>());
        vm.ResetCommand.Execute(null);
    }
}
