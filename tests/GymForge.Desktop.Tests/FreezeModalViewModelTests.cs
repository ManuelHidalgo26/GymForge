using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Memberships;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Modal "Congelar membresía": fechas en día local, cupo anual a la vista y errores sin romper.</summary>
public class FreezeModalViewModelTests
{
    // "Hoy" local según la empresa: deliberadamente distinto de la fecha real de la PC.
    private static readonly DateOnly Today = new(2026, 3, 1);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly List<string> _events = [];

    private static MembershipDto MembershipEndingOn(DateOnly? endDate) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mensual",
        new DateOnly(2026, 1, 1), endDate, MembershipStatus.Active, AutoRenew: true,
        VisitsRemaining: null, FreezeStart: null, FreezeEnd: null, CancelDate: null, CancelReason: null);

    private static FreezeQuotaDto QuotaUsing(int usedDays, int maxDays = 90) =>
        new(Today, Today.Year, maxDays, usedDays);

    private FreezeModalViewModel Sut(
        MembershipDto? membership = null, FreezeQuotaDto? quota = null)
    {
        var vm = new FreezeModalViewModel(
            _mediator, membership ?? MembershipEndingOn(new DateOnly(2026, 12, 31)), quota ?? QuotaUsing(0));
        vm.MembershipFrozen += () => _events.Add("frozen");
        vm.Cancelled += () => _events.Add("cancelled");
        return vm;
    }

    // ── Estado inicial ────────────────────────────────────────────────────────

    [Fact]
    public void Opens_ProposingThirtyDaysFromTheLocalToday()
    {
        var vm = Sut();

        vm.StartDate.Should().Be(Today);
        vm.ReactivationDate.Should().Be(Today.AddDays(30));
        vm.RequestedDays.Should().Be(30);
        vm.ValidationMessage.Should().BeNull();
        vm.ConfirmCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void Opens_ProposingOnlyWhatIsLeftOfTheQuota()
    {
        var vm = Sut(quota: QuotaUsing(usedDays: 80));

        vm.ReactivationDate.Should().Be(Today.AddDays(10));
        vm.ConfirmCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ShowsTheDaysUsedAndLeftOfTheYear()
    {
        var vm = Sut(quota: QuotaUsing(usedDays: 20));

        vm.QuotaSummary.Should().Contain("20 de 90").And.Contain("2026").And.Contain("quedan 70");
    }

    [Fact]
    public void ShowsWhenTheMembershipWillExpireAfterTheFreeze()
    {
        var vm = Sut(MembershipEndingOn(new DateOnly(2026, 12, 31)));

        vm.NewEndDate.Should().Be(new DateOnly(2027, 1, 30), "el vencimiento se corre los 30 días congelados");
    }

    [Fact]
    public void MembershipWithoutEndDate_HasNoNewEndDate()
    {
        var vm = Sut(MembershipEndingOn(null));

        vm.NewEndDate.Should().BeNull();
    }

    [Fact]
    public void NoQuotaLeft_ExplainsItAndCannotBeConfirmed()
    {
        var vm = Sut(quota: QuotaUsing(usedDays: 90));

        vm.ReactivationDate.Should().BeNull();
        vm.ValidationMessage.Should().Contain("No quedan días").And.Contain("2026");
        vm.ConfirmCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Validación en vivo ────────────────────────────────────────────────────

    [Fact]
    public void RangeOverTheCap_ShowsTheCapMessage_AndCannotBeConfirmed()
    {
        var vm = Sut(quota: QuotaUsing(usedDays: 20));

        vm.ReactivationDate = Today.AddDays(71);

        vm.ValidationMessage.Should().Contain("tope de 90").And.Contain("te quedan 70").And.Contain("71");
        vm.ConfirmCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void RangeExactlyAtTheRemainingDays_IsAllowed()
    {
        var vm = Sut(quota: QuotaUsing(usedDays: 20));

        vm.ReactivationDate = Today.AddDays(70);

        vm.ValidationMessage.Should().BeNull();
        vm.ConfirmCommand.CanExecute(null).Should().BeTrue();
    }

    [Fact]
    public void ChangingTheDate_UpdatesDaysAndNewEndDate()
    {
        var vm = Sut(MembershipEndingOn(new DateOnly(2026, 12, 31)));

        vm.ReactivationDate = Today.AddDays(15);

        vm.RequestedDays.Should().Be(15);
        vm.NewEndDate.Should().Be(new DateOnly(2027, 1, 15));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DateNotAfterToday_CannotBeConfirmed(int offsetDays)
    {
        var vm = Sut();

        vm.ReactivationDate = Today.AddDays(offsetDays);

        vm.ValidationMessage.Should().Contain("posterior a hoy");
        vm.NewEndDate.Should().BeNull();
        vm.ConfirmCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void ClearedDate_AsksForOne()
    {
        var vm = Sut();

        vm.ReactivationDate = null;

        vm.RequestedDays.Should().Be(0);
        vm.ValidationMessage.Should().Contain("Elegí la fecha");
        vm.ConfirmCommand.CanExecute(null).Should().BeFalse();
    }

    // ── Confirmar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Confirm_FreezesFromTheLocalToday_ToTheChosenDate()
    {
        var membership = MembershipEndingOn(new DateOnly(2026, 12, 31));
        var vm = Sut(membership);
        vm.ReactivationDate = Today.AddDays(14);
        vm.Reason = "  Viaje por trabajo ";

        await vm.ConfirmCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<FreezeMembershipCommand>(c =>
                c.MembershipId == membership.Id &&
                c.FreezeStart == Today &&
                c.FreezeEnd == Today.AddDays(14) &&
                c.Reason == "Viaje por trabajo"),
            Arg.Any<CancellationToken>());
        _events.Should().Equal("frozen");
        vm.ErrorMessage.Should().BeNull();
        vm.IsSaving.Should().BeFalse();
    }

    [Fact]
    public async Task Confirm_WithoutReason_UsesTheDefaultOne()
    {
        var vm = Sut();
        vm.Reason = "   ";

        await vm.ConfirmCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<FreezeMembershipCommand>(c => c.Reason == FreezeModalViewModel.DefaultReason),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_WhenTheDomainRefuses_ShowsItsMessage_KeepsTheModalOpen_AndDoesNotThrow()
    {
        // La pantalla pudo quedar desactualizada: el dominio es la autoridad sobre el tope.
        const string capMessage = "Supera el tope de 90 días de congelamiento por año (usados 85, solicitados 30).";
        _mediator.Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException(capMessage));
        var vm = Sut();

        var act = () => vm.ConfirmCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.ErrorMessage.Should().Be(capMessage);
        vm.IsSaving.Should().BeFalse();
        _events.Should().BeEmpty("el modal sigue abierto para corregir las fechas");
    }

    [Fact]
    public async Task Confirm_AfterAnError_ClearsItWhenTheRetrySucceeds()
    {
        _mediator.Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Solo se pueden congelar membresías activas."));
        var vm = Sut();
        await vm.ConfirmCommand.ExecuteAsync(null);
        vm.ErrorMessage.Should().NotBeNull();

        _mediator.Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .Returns(MembershipEndingOn(null));
        await vm.ConfirmCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().BeNull();
        _events.Should().Equal("frozen");
    }

    // ── Cancelar ──────────────────────────────────────────────────────────────

    [Fact]
    public void Cancel_RaisesCancelled_AndFreezesNothing()
    {
        var vm = Sut();

        vm.CancelCommand.Execute(null);

        _events.Should().Equal("cancelled");
        _mediator.ReceivedCalls().Should().BeEmpty();
    }
}
