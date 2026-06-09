using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Cash;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class CashCommandsTests
{
    private readonly IShiftRepository _repo = Substitute.For<IShiftRepository>();

    // ── Abrir caja ──────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenShift_NoOpenShift_CreatesAndReturnsDto()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        _repo.GetOpenForSiteAsync(siteId, Arg.Any<CancellationToken>()).Returns((Shift?)null);

        var dto = await new OpenShiftCommandHandler(_repo).Handle(
            new OpenShiftCommand(companyId, siteId, Guid.NewGuid(), 5_000m), CancellationToken.None);

        dto.OpeningCash.Should().Be(5_000m);
        dto.ExpectedCash.Should().Be(5_000m);
        dto.Status.Should().Be(ShiftStatus.Open);
        await _repo.Received(1).AddAsync(Arg.Any<Shift>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenShift_AlreadyOpenForSite_Throws()
    {
        var siteId = Guid.NewGuid();
        _repo.GetOpenForSiteAsync(siteId, Arg.Any<CancellationToken>())
            .Returns(Shift.Open(Guid.NewGuid(), siteId, Guid.NewGuid(), 0m));

        var act = () => new OpenShiftCommandHandler(_repo).Handle(
            new OpenShiftCommand(Guid.NewGuid(), siteId, Guid.NewGuid(), 1_000m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Movimiento de caja ──────────────────────────────────────────────────

    [Fact]
    public async Task AddCashMovement_UpdatesExpectedCash()
    {
        var shift = Shift.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m);
        _repo.GetByIdAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        var afterIncome = await new AddCashMovementCommandHandler(_repo).Handle(
            new AddCashMovementCommand(shift.Id, CashMovementType.Income, CashMovementCategory.Sale, 3_000m),
            CancellationToken.None);
        afterIncome.ExpectedCash.Should().Be(13_000m);

        var afterExpense = await new AddCashMovementCommandHandler(_repo).Handle(
            new AddCashMovementCommand(shift.Id, CashMovementType.Expense, CashMovementCategory.PettyCash, 1_000m),
            CancellationToken.None);
        afterExpense.ExpectedCash.Should().Be(12_000m);
        afterExpense.Movements.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddCashMovement_ClosedShift_Throws()
    {
        var shift = Shift.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m);
        shift.BeginClose(0m, 0m);
        shift.ConfirmClose();
        _repo.GetByIdAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        var act = () => new AddCashMovementCommandHandler(_repo).Handle(
            new AddCashMovementCommand(shift.Id, CashMovementType.Income, CashMovementCategory.Sale, 100m),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Cerrar caja (arqueo) ────────────────────────────────────────────────

    [Fact]
    public async Task CloseShift_ComputesDifferenceAgainstExpected()
    {
        var shift = Shift.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10_000m);
        shift.Movements.Add(CashMovement.Create(shift.Id, CashMovementType.Income, CashMovementCategory.Sale, 5_000m));
        _repo.GetByIdAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        // Sistema espera 15.000; el cajero declara 14.500 → faltante de 500.
        var dto = await new CloseShiftCommandHandler(_repo).Handle(
            new CloseShiftCommand(shift.Id, 14_500m, "Arqueo nocturno"), CancellationToken.None);

        dto.Status.Should().Be(ShiftStatus.Closed);
        dto.ExpectedCash.Should().Be(15_000m);
        dto.DeclaredCash.Should().Be(14_500m);
        dto.Difference.Should().Be(-500m);
    }

    // ── Validators ──────────────────────────────────────────────────────────

    [Fact]
    public void OpenShift_NegativeOpeningCash_Fails() =>
        new OpenShiftCommandValidator()
            .Validate(new OpenShiftCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), -1m))
            .IsValid.Should().BeFalse();

    [Fact]
    public void AddCashMovement_NonPositiveAmount_Fails() =>
        new AddCashMovementCommandValidator()
            .Validate(new AddCashMovementCommand(Guid.NewGuid(), CashMovementType.Income, CashMovementCategory.Sale, 0m))
            .IsValid.Should().BeFalse();
}
