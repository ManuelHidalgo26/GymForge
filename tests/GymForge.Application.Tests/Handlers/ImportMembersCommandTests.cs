using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class ImportMembersCommandTests
{
    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();
    private readonly CurrentLicense _license = new();

    private ImportMembersCommandHandler Sut() => new(_repo, _license);

    private static ImportMemberRow Row(string doc, string first = "Ana", string last = "García") =>
        new(first, last, doc, null, null, null);

    [Fact]
    public async Task Handle_ValidRows_PersistsEachAndSavesOnce()
    {
        var cmd = new ImportMembersCommand(Guid.NewGuid(), Guid.NewGuid(),
            new[] { Row("1"), Row("2"), Row("3") });

        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Imported.Should().Be(3);
        result.Skipped.Should().Be(0);
        await _repo.Received(3).AddAsync(Arg.Any<Member>(), Arg.Any<CancellationToken>());
        await _repo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ImportedMembers_AreActive()
    {
        var cmd = new ImportMembersCommand(Guid.NewGuid(), Guid.NewGuid(), new[] { Row("1") });

        await Sut().Handle(cmd, CancellationToken.None);

        await _repo.Received(1).AddAsync(
            Arg.Is<Member>(m => m.Status == MemberStatus.Active && m.Source == MemberSource.Other),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DuplicateInDatabase_IsSkipped()
    {
        var companyId = Guid.NewGuid();
        _repo.FindByDocumentAsync(DocumentType.DNI, "1", companyId, Arg.Any<CancellationToken>())
            .Returns(Member.Create(companyId, Guid.NewGuid(), "X", "Y", DocumentType.DNI, "1", Gender.Male));

        var cmd = new ImportMembersCommand(companyId, Guid.NewGuid(), new[] { Row("1"), Row("2") });

        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("ya existe");
    }

    [Fact]
    public async Task Handle_DuplicateWithinFile_IsSkipped()
    {
        var cmd = new ImportMembersCommand(Guid.NewGuid(), Guid.NewGuid(),
            new[] { Row("7"), Row("7") });

        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
        result.Errors.Should().ContainSingle().Which.Should().Contain("repetido");
    }

    [Fact]
    public async Task Handle_RespectsLicenseLimit()
    {
        var companyId = Guid.NewGuid();
        // Ya hay socios hasta el tope Free (50): nada se importa.
        _repo.CountByCompanyAsync(companyId, Arg.Any<CancellationToken>())
            .Returns(LicenseState.FreeMaxMembers);

        var cmd = new ImportMembersCommand(companyId, Guid.NewGuid(), new[] { Row("1"), Row("2") });

        var result = await Sut().Handle(cmd, CancellationToken.None);

        result.Imported.Should().Be(0);
        result.Skipped.Should().Be(2);
        await _repo.DidNotReceive().AddAsync(Arg.Any<Member>(), Arg.Any<CancellationToken>());
    }
}
