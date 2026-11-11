using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Sales;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Cash;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>
/// Venta de membresía a un socio congelado o suspendido: la venta se concreta pero el socio no se reactiva,
/// y el cajero tiene que enterarse con un aviso informativo (no bloqueante).
/// </summary>
public class RegisterSaleViewModelNoticeTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private async Task<RegisterSaleViewModel> SutAsync(MemberStatus memberStatus)
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var session = await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
        var receipts = new ReceiptService(Substitute.For<IServiceScopeFactory>());

        return new RegisterSaleViewModel(_mediator, Substitute.For<IMemberRepository>(), session, receipts)
        {
            SelectedMember = MemberWithStatus(memberStatus),
            SelectedPlan = new MembershipTypeDto(
                Guid.NewGuid(), "Mensual", 35_000m, "1 mes", MembershipBasis.Renewal, true, VisitsIncluded: null),
        };
    }

    private static MemberDto MemberWithStatus(MemberStatus status) =>
        MemberDto.FromEntity(
            Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30123456", Gender.Female))
        with { Status = status };

    private void SaleSucceeds() =>
        _mediator.Send(Arg.Any<SellMembershipCommand>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentDto(Guid.NewGuid(), null, 35_000m, PaymentMethod.Cash, DateTime.UtcNow,
                PaymentStatus.Completed, null, null, []));

    [Theory]
    [InlineData(MemberStatus.Frozen, "congelado")]
    [InlineData(MemberStatus.Suspended, "suspendido")]
    public async Task Confirm_MembershipToFrozenOrSuspendedMember_RegistersAndWarnsTheCashier(
        MemberStatus status, string expectedWord)
    {
        var vm = await SutAsync(status);
        SaleSucceeds();
        var registered = false;
        vm.Registered += () => registered = true;

        await vm.ConfirmCommand.ExecuteAsync(null);

        registered.Should().BeTrue("el aviso no bloquea la venta");
        vm.ErrorMessage.Should().BeNull();
        vm.PostSaleNotice.Should().Contain($"sigue {expectedWord}").And.Contain("ficha");
    }

    [Theory]
    [InlineData(MemberStatus.Active)]
    [InlineData(MemberStatus.Expired)]
    [InlineData(MemberStatus.Overdue)]
    public async Task Confirm_MembershipToAnyOtherMember_GivesNoNotice(MemberStatus status)
    {
        var vm = await SutAsync(status);
        SaleSucceeds();

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.PostSaleNotice.Should().BeNull();
    }

    [Fact]
    public async Task Confirm_WhenTheSaleFails_GivesNoNoticeAndShowsTheError()
    {
        var vm = await SutAsync(MemberStatus.Frozen);
        _mediator.Send(Arg.Any<SellMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("El plan seleccionado no existe."));

        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.PostSaleNotice.Should().BeNull("la venta no se concretó");
        vm.ErrorMessage.Should().Be("El plan seleccionado no existe.");
    }
}
