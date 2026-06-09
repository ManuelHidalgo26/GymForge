using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Staff;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class AuthenticateStaffCommandTests
{
    private readonly IStaffRepository _repo = Substitute.For<IStaffRepository>();
    private readonly IPinHasher _hasher = Substitute.For<IPinHasher>();

    private AuthenticateStaffCommandHandler Sut() => new(_repo, _hasher);

    [Fact]
    public async Task Handle_CorrectPin_ReturnsStaffDto()
    {
        var companyId = Guid.NewGuid();
        var admin = Domain.Entities.Staff.Create(companyId, "Admin", "GymForge", StaffRole.Admin, "HASH-ADMIN");
        var trainer = Domain.Entities.Staff.Create(companyId, "Pepe", "Pesas", StaffRole.Trainer, "HASH-TRAINER");

        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { admin, trainer });
        _hasher.Verify("1234", "HASH-ADMIN").Returns(true);
        _hasher.Verify("1234", "HASH-TRAINER").Returns(false);

        var dto = await Sut().Handle(new AuthenticateStaffCommand(companyId, "1234"), CancellationToken.None);

        dto.Should().NotBeNull();
        dto!.FullName.Should().Be("Admin GymForge");
        dto.Role.Should().Be(StaffRole.Admin);
    }

    [Fact]
    public async Task Handle_WrongPin_ReturnsNull()
    {
        var companyId = Guid.NewGuid();
        var admin = Domain.Entities.Staff.Create(companyId, "Admin", "GymForge", StaffRole.Admin, "HASH-ADMIN");

        _repo.GetActiveByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(new List<Staff> { admin });
        _hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var dto = await Sut().Handle(new AuthenticateStaffCommand(companyId, "0000"), CancellationToken.None);

        dto.Should().BeNull();
    }

    [Fact]
    public async Task Handle_EmptyPin_ReturnsNullWithoutQuery()
    {
        var dto = await Sut().Handle(new AuthenticateStaffCommand(Guid.NewGuid(), "  "), CancellationToken.None);

        dto.Should().BeNull();
        await _repo.DidNotReceive().GetActiveByCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
