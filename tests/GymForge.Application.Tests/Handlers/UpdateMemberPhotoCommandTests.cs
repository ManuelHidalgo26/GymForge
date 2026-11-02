using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class UpdateMemberPhotoCommandTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private const string PhotoPath = @"C:\Users\recepcion\AppData\Local\GymForge\fotos\ana.jpg";

    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();

    private UpdateMemberPhotoCommandHandler Handler() => new(_repo);

    private Member HaveMember(Guid? companyId = null)
    {
        var member = Member.Create(companyId ?? CompanyId, Guid.NewGuid(), "Ana", "García",
            DocumentType.DNI, "30123456", Gender.Female);
        _repo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        return member;
    }

    [Fact]
    public async Task Handle_SetsThePhotoOfTheRightMember_AndSaves()
    {
        var ana = HaveMember();
        var other = HaveMember();

        var dto = await Handler().Handle(new UpdateMemberPhotoCommand(CompanyId, ana.Id, PhotoPath), CancellationToken.None);

        dto.Id.Should().Be(ana.Id);
        dto.PhotoUrl.Should().Be(PhotoPath);
        ana.PhotoUrl.Should().Be(PhotoPath);
        other.PhotoUrl.Should().BeNull();
        _repo.Received(1).Update(ana);
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReplacesThePreviousPhoto()
    {
        var ana = HaveMember();
        ana.UpdatePhoto(@"C:\viejo\ana.png");

        await Handler().Handle(new UpdateMemberPhotoCommand(CompanyId, ana.Id, PhotoPath), CancellationToken.None);

        ana.PhotoUrl.Should().Be(PhotoPath);
    }

    [Fact]
    public async Task Handle_UnknownMember_ThrowsNotFound()
    {
        var missing = Guid.NewGuid();
        _repo.GetByIdAsync(missing, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = () => Handler().Handle(new UpdateMemberPhotoCommand(CompanyId, missing, PhotoPath), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_MemberOfAnotherCompany_IsTreatedAsNotFound_AndNothingIsSaved()
    {
        var foreign = HaveMember(companyId: Guid.NewGuid());

        var act = () => Handler().Handle(new UpdateMemberPhotoCommand(CompanyId, foreign.Id, PhotoPath), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        foreign.PhotoUrl.Should().BeNull();
        await _repo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(PhotoPath, true)]
    public void Validator_RequiresAPath(string path, bool valid)
    {
        new UpdateMemberPhotoCommandValidator()
            .Validate(new UpdateMemberPhotoCommand(CompanyId, Guid.NewGuid(), path))
            .IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RejectsAPathLongerThanTheColumn()
    {
        new UpdateMemberPhotoCommandValidator()
            .Validate(new UpdateMemberPhotoCommand(CompanyId, Guid.NewGuid(), new string('a', 501)))
            .IsValid.Should().BeFalse();
    }
}
