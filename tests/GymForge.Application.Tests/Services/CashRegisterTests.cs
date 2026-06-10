using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Services;

public class CashRegisterTests
{
    private readonly IShiftRepository _shiftRepo = Substitute.For<IShiftRepository>();
    private CashRegister Sut() => new(_shiftRepo);

    [Fact]
    public async Task PostIfCash_CashOnOpenShift_AddsIncomeMovement()
    {
        var shift = Shift.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m);
        _shiftRepo.GetByIdAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        await Sut().PostIfCashAsync(PaymentMethod.Cash, shift.Id,
            CashMovementCategory.Membership, 5_000m, Guid.NewGuid());

        shift.Movements.Should().ContainSingle();
        shift.CashIn.Should().Be(5_000m);
        _shiftRepo.Received(1).Update(shift);
    }

    [Fact]
    public async Task PostIfCash_CardPayment_DoesNothing()
    {
        await Sut().PostIfCashAsync(PaymentMethod.CreditCard, Guid.NewGuid(),
            CashMovementCategory.Sale, 5_000m, null);

        await _shiftRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostIfCash_NoShift_DoesNothing()
    {
        await Sut().PostIfCashAsync(PaymentMethod.Cash, null,
            CashMovementCategory.Sale, 5_000m, null);

        await _shiftRepo.DidNotReceive().GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PostIfCash_ClosedShift_DoesNotAddMovement()
    {
        var shift = Shift.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0m);
        shift.BeginClose(0m, 0m);
        shift.ConfirmClose();
        _shiftRepo.GetByIdAsync(shift.Id, Arg.Any<CancellationToken>()).Returns(shift);

        await Sut().PostIfCashAsync(PaymentMethod.Cash, shift.Id,
            CashMovementCategory.Membership, 5_000m, null);

        shift.Movements.Should().BeEmpty();
        _shiftRepo.DidNotReceive().Update(Arg.Any<Shift>());
    }
}
