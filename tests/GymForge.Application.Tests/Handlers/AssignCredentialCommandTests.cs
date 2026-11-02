using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class AssignCredentialCommandTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();

    private AssignCredentialCommandHandler Handler() => new(_repo);

    private static Member NewMember(Guid? companyId = null) =>
        Member.Create(companyId ?? CompanyId, Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30123456", Gender.Female);

    private Member HaveMember(Member member)
    {
        _repo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    [Fact]
    public async Task Handle_AssignsTheTrimmedCredential_AndSaves()
    {
        var member = HaveMember(NewMember());

        var dto = await Handler().Handle(
            new AssignCredentialCommand(CompanyId, member.Id, "  CARNET-77 "), CancellationToken.None);

        dto.TagSerial.Should().Be("CARNET-77");
        member.TagSerial.Should().Be("CARNET-77");
        _repo.Received(1).Update(member);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ChecksTheUniquenessInTheCompany_ExcludingTheMemberItself()
    {
        var member = HaveMember(NewMember());

        await Handler().Handle(new AssignCredentialCommand(CompanyId, member.Id, "CARNET-77"), CancellationToken.None);

        await _repo.Received(1).IsTagSerialInUseAsync(
            CompanyId, "CARNET-77", member.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CredentialOfAnotherMember_IsRejectedAndNothingIsSaved()
    {
        var member = HaveMember(NewMember());
        _repo.IsTagSerialInUseAsync(CompanyId, "CARNET-77", member.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = () => Handler().Handle(
            new AssignCredentialCommand(CompanyId, member.Id, "CARNET-77"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*credencial*otro socio*");
        member.TagSerial.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReassigningTheSameCredentialToTheSameMember_IsAccepted()
    {
        var member = HaveMember(NewMember());
        member.EnrollTag("CARNET-77");

        var dto = await Handler().Handle(
            new AssignCredentialCommand(CompanyId, member.Id, "CARNET-77"), CancellationToken.None);

        dto.TagSerial.Should().Be("CARNET-77");
    }

    [Fact]
    public async Task Handle_ReplacesThePreviousCredential()
    {
        var member = HaveMember(NewMember());
        member.EnrollTag("VIEJO");

        await Handler().Handle(new AssignCredentialCommand(CompanyId, member.Id, "NUEVO"), CancellationToken.None);

        member.TagSerial.Should().Be("NUEVO");
    }

    [Fact]
    public async Task Handle_UnknownMember_ThrowsNotFound()
    {
        var missingId = Guid.NewGuid();
        _repo.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = () => Handler().Handle(
            new AssignCredentialCommand(CompanyId, missingId, "CARNET-77"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_MemberOfAnotherCompany_IsTreatedAsNotFound()
    {
        var foreign = HaveMember(NewMember(companyId: Guid.NewGuid()));

        var act = () => Handler().Handle(
            new AssignCredentialCommand(CompanyId, foreign.Id, "CARNET-77"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        foreign.TagSerial.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("CARNET-77", true)]
    public void Validator_RequiresACredential(string tagSerial, bool valid)
    {
        new AssignCredentialCommandValidator()
            .Validate(new AssignCredentialCommand(CompanyId, Guid.NewGuid(), tagSerial))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RejectsACredentialLongerThanTheColumn()
    {
        new AssignCredentialCommandValidator()
            .Validate(new AssignCredentialCommand(CompanyId, Guid.NewGuid(), new string('9', 51)))
            .IsValid.Should().BeFalse();
    }
}
