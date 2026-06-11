using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class CreateMemberCommandTests
{
    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();
    private readonly CurrentLicense _license = new();

    private static CreateMemberCommand ValidCommand() => new(
        CompanyId: Guid.NewGuid(),
        SiteId: Guid.NewGuid(),
        FirstName: "Ana",
        LastName: "García",
        DocumentType: DocumentType.DNI,
        DocumentNumber: "30123456",
        Gender: Gender.Female,
        Email: "ana@test.com",
        Mobile: "+5491123456789",
        BirthDate: new DateOnly(1990, 5, 20));

    [Fact]
    public async Task Handle_PersistsMemberAndReturnsDto()
    {
        var cmd = ValidCommand();
        var handler = new CreateMemberCommandHandler(_repo, _license);

        var dto = await handler.Handle(cmd, CancellationToken.None);

        dto.FullName.Should().Be("Ana García");
        dto.DocumentNumber.Should().Be("30123456");
        dto.Email.Should().Be("ana@test.com");
        dto.BirthDate.Should().Be(new DateOnly(1990, 5, 20));

        await _repo.Received(1).AddAsync(
            Arg.Is<Member>(m =>
                m.DocumentNumber == "30123456" &&
                m.CompanyId == cmd.CompanyId &&
                m.BirthDate == new DateOnly(1990, 5, 20) &&
                m.Source == MemberSource.WalkIn),
            Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActivateImmediately_CreatesActiveMember()
    {
        var cmd = ValidCommand() with { ActivateImmediately = true };
        var dto = await new CreateMemberCommandHandler(_repo, _license).Handle(cmd, CancellationToken.None);

        dto.Status.Should().Be(MemberStatus.Active);
        await _repo.Received(1).AddAsync(
            Arg.Is<Member>(m => m.Status == MemberStatus.Active && m.JoinDate != null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LimiteDeLicenciaAlcanzado_BloqueaElAlta()
    {
        var cmd = ValidCommand();
        _repo.CountByCompanyAsync(cmd.CompanyId, Arg.Any<CancellationToken>())
            .Returns(LicenseState.FreeMaxMembers);   // ya está en el tope Free (50)

        var act = () => new CreateMemberCommandHandler(_repo, _license)
            .Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*límite de 50 socios*");
        await _repo.DidNotReceive().AddAsync(Arg.Any<Member>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_EmptyFirstName_Fails()
    {
        var cmd = ValidCommand() with { FirstName = "" };
        new CreateMemberCommandValidator().Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_InvalidEmail_Fails()
    {
        var cmd = ValidCommand() with { Email = "no-es-un-email" };
        new CreateMemberCommandValidator().Validate(cmd).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validator_ValidCommand_Passes()
    {
        new CreateMemberCommandValidator().Validate(ValidCommand()).IsValid.Should().BeTrue();
    }
}
