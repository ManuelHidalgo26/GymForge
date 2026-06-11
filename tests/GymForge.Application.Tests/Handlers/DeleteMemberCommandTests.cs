using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class DeleteMemberCommandTests
{
    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();

    private static Member MakeMember() =>
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Test", "Borrable",
            DocumentType.DNI, "99000111", Gender.Male);

    [Fact]
    public async Task Handle_NoActivity_RemovesMember()
    {
        var member = MakeMember();
        _repo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _repo.HasActivityAsync(member.Id, Arg.Any<CancellationToken>()).Returns(false);

        await new DeleteMemberCommandHandler(_repo).Handle(
            new DeleteMemberCommand(member.Id), CancellationToken.None);

        _repo.Received(1).Remove(member);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithActivity_ThrowsAndDoesNotRemove()
    {
        var member = MakeMember();
        _repo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _repo.HasActivityAsync(member.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => new DeleteMemberCommandHandler(_repo).Handle(
            new DeleteMemberCommand(member.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _repo.DidNotReceive().Remove(Arg.Any<Member>());
    }
}
