using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Staff;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ChangePinCommandTests
{
    private readonly IStaffRepository _repo = Substitute.For<IStaffRepository>();
    private readonly IPinHasher _hasher = Substitute.For<IPinHasher>();

    private ChangePinCommandHandler Sut() => new(_repo, _hasher);

    private static Staff Admin(string hash) =>
        Staff.Create(Guid.NewGuid(), "Admin", "GymForge", StaffRole.Admin, hash);

    [Fact]
    public async Task Handle_CorrectCurrentPin_UpdatesHashAndPersists()
    {
        var companyId = Guid.NewGuid();
        var admin = Admin("HASH-OLD");

        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { admin });
        _hasher.Verify("1234", "HASH-OLD").Returns(true);
        _hasher.Hash("5678").Returns("HASH-NEW");

        await Sut().Handle(new ChangePinCommand(companyId, "1234", "5678"), CancellationToken.None);

        admin.PinCodeHash.Should().Be("HASH-NEW");
        _repo.Received(1).Update(admin);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WrongCurrentPin_ThrowsAndDoesNotPersist()
    {
        var companyId = Guid.NewGuid();
        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { Admin("HASH-OLD") });
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var act = () => Sut().Handle(new ChangePinCommand(companyId, "0000", "5678"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*PIN actual es incorrecto*");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NewPinCollidesWithOtherStaff_Throws()
    {
        var companyId = Guid.NewGuid();
        var admin = Admin("HASH-ADMIN");
        var trainer = Admin("HASH-TRAINER");

        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { admin, trainer });
        _hasher.Verify("1234", "HASH-ADMIN").Returns(true);
        _hasher.Verify("5678", "HASH-TRAINER").Returns(true);   // ya lo usa otro

        var act = () => Sut().Handle(new ChangePinCommand(companyId, "1234", "5678"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ya lo usa otro*");
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("12", false)]        // muy corto
    [InlineData("123456789", false)] // muy largo
    [InlineData("12ab", false)]      // no numérico
    [InlineData("1234", true)]
    [InlineData("00000000", true)]
    public void Validator_EnforcesPinShape(string newPin, bool expectedValid)
    {
        var result = new ChangePinCommandValidator()
            .Validate(new ChangePinCommand(Guid.NewGuid(), "9999", newPin));
        result.IsValid.Should().Be(expectedValid);
    }

    [Fact]
    public void Validator_NewPinEqualsCurrent_Fails()
    {
        new ChangePinCommandValidator()
            .Validate(new ChangePinCommand(Guid.NewGuid(), "1234", "1234"))
            .IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task CheckDefaultPin_StillDefault_ReturnsTrue()
    {
        var companyId = Guid.NewGuid();
        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { Admin("HASH-DEFAULT") });
        _hasher.Verify("1234", "HASH-DEFAULT").Returns(true);

        var result = await new CheckDefaultPinQueryHandler(_repo, _hasher)
            .Handle(new CheckDefaultPinQuery(companyId), CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CheckDefaultPin_Changed_ReturnsFalse()
    {
        var companyId = Guid.NewGuid();
        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { Admin("HASH-REAL") });
        _hasher.Verify("1234", "HASH-REAL").Returns(false);

        var result = await new CheckDefaultPinQueryHandler(_repo, _hasher)
            .Handle(new CheckDefaultPinQuery(companyId), CancellationToken.None);

        result.Should().BeFalse();
    }
}
