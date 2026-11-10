using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Charges;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>El modal de cobro tiene que mostrarle al cajero por qué el handler rechazó el pago, no tragárselo.</summary>
public class PaymentModalViewModelTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private async Task<PaymentModalViewModel> SutAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var session = await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
        var vm = new PaymentModalViewModel(_mediator, session, new ReceiptService(Substitute.For<IServiceScopeFactory>()));
        vm.PreSelectMember(Guid.NewGuid(), "Ana García", 5_000m);
        return vm;
    }

    [Theory]
    [InlineData("saldado")]
    [InlineData("dado de baja")]
    [InlineData("reembolsado")]
    public async Task Confirm_WhenTheChargeIsNotCollectable_ShowsTheHandlerMessage(string state)
    {
        var vm = await SutAsync();
        var message = $"El cargo \"Cuota\" está {state} y no admite cobro.";
        _mediator.Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(message));
        var registered = false;
        vm.PaymentRegistered += _ => registered = true;

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be(message);
        registered.Should().BeFalse();
        vm.IsSaving.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WhenAnUnexpectedErrorHappens_ShowsAGenericPrefixedMessage()
    {
        var vm = await SutAsync();
        _mediator.Send(Arg.Any<ProcessPaymentCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("base bloqueada"));

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be("Error al registrar pago: base bloqueada");
    }
}
