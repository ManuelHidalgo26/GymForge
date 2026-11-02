using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Access;

public class CredentialMatchTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();

    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();

    private static Member NewMember(string dni) =>
        Member.Create(CompanyId, Guid.NewGuid(), "Ana", "García", DocumentType.DNI, dni, Gender.Female);

    // ── LooksLikeDni ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("40123456", true)]
    [InlineData("4012345", true)]
    [InlineData("1234567", true)]
    [InlineData("12345678", true)]
    [InlineData("123456", false)]        // 6 dígitos
    [InlineData("123456789", false)]     // 9 dígitos
    [InlineData("ABC123", false)]
    [InlineData("4012345A", false)]
    [InlineData("40.123.456", false)]
    [InlineData(" 4012345", false)]
    [InlineData("4012345٣", false)] // dígito arábigo: no es ASCII
    [InlineData("", false)]
    [InlineData(null, false)]
    public void LooksLikeDni_SoloAceptaSieteOOchoDigitosAscii(string? code, bool expected)
    {
        CredentialMatch.LooksLikeDni(code).Should().Be(expected);
    }

    // ── FindMemberAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task FindMember_PruebaPrimeroLaCredencialDelGimnasio()
    {
        var owner = NewMember("30111222");
        _members.FindByTagSerialAsync("12345678", CompanyId, Arg.Any<CancellationToken>()).Returns(owner);

        var found = await CredentialMatch.FindMemberAsync(_members, "12345678", CompanyId);

        found.Should().BeSameAs(owner);
        await _members.DidNotReceive().FindByDocumentAsync(
            Arg.Any<DocumentType>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindMember_SinTag_CaeAlDniSiTieneFormaDeDni()
    {
        var byDni = NewMember("12345678");
        _members.FindByTagSerialAsync("12345678", CompanyId, Arg.Any<CancellationToken>()).ReturnsNull();
        _members.FindByDocumentAsync(DocumentType.DNI, "12345678", CompanyId, Arg.Any<CancellationToken>())
            .Returns(byDni);

        var found = await CredentialMatch.FindMemberAsync(_members, "12345678", CompanyId);

        found.Should().BeSameAs(byDni);
    }

    [Fact]
    public async Task FindMember_SinTagYSinFormaDeDni_NoConsultaPorDocumento()
    {
        _members.FindByTagSerialAsync("CARNET-9", CompanyId, Arg.Any<CancellationToken>()).ReturnsNull();

        var found = await CredentialMatch.FindMemberAsync(_members, "CARNET-9", CompanyId);

        found.Should().BeNull();
        await _members.DidNotReceive().FindByDocumentAsync(
            Arg.Any<DocumentType>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task FindMember_CodigoEnBlanco_NoConsultaNada(string code)
    {
        var found = await CredentialMatch.FindMemberAsync(_members, code, CompanyId);

        found.Should().BeNull();
        await _members.DidNotReceive().FindByTagSerialAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _members.DidNotReceive().FindByDocumentAsync(
            Arg.Any<DocumentType>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
