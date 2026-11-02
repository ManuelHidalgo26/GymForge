using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;
using NSubstitute.ReturnsExtensions;

namespace GymForge.Application.Tests.Handlers;

public class FindMemberByCredentialQueryTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid OtherCompanyId = Guid.NewGuid();

    private readonly IMemberRepository _repo = Substitute.For<IMemberRepository>();

    private Task<MemberDto?> RunAsync(Guid companyId, string code) =>
        new FindMemberByCredentialQueryHandler(_repo)
            .Handle(new FindMemberByCredentialQuery(companyId, code), CancellationToken.None);

    private static Member NewMember(string dni) =>
        Member.Create(CompanyId, Guid.NewGuid(), "Ana", "García", DocumentType.DNI, dni, Gender.Female);

    [Fact]
    public async Task Handle_CodigoDeUnaCredencial_DevuelveAlSocioDuenio()
    {
        var member = NewMember("30123456");
        member.EnrollTag("CARNET-77");
        _repo.FindByTagSerialAsync("CARNET-77", CompanyId, Arg.Any<CancellationToken>()).Returns(member);

        var dto = await RunAsync(CompanyId, "CARNET-77");

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(member.Id);
        dto.TagSerial.Should().Be("CARNET-77");
    }

    [Fact]
    public async Task Handle_SinCredencial_CaeAlDniSiElCodigoTieneFormaDeDni()
    {
        var member = NewMember("30123456");
        _repo.FindByTagSerialAsync("30123456", CompanyId, Arg.Any<CancellationToken>()).ReturnsNull();
        _repo.FindByDocumentAsync(DocumentType.DNI, "30123456", CompanyId, Arg.Any<CancellationToken>())
            .Returns(member);

        var dto = await RunAsync(CompanyId, "30123456");

        dto!.Id.Should().Be(member.Id);
        dto.DocumentNumber.Should().Be("30123456");
    }

    [Fact]
    public async Task Handle_CodigoQueNoEsDeNadie_DevuelveNull()
    {
        _repo.FindByTagSerialAsync("DESCONOCIDO", CompanyId, Arg.Any<CancellationToken>()).ReturnsNull();

        (await RunAsync(CompanyId, "DESCONOCIDO")).Should().BeNull();
    }

    [Fact]
    public async Task Handle_CodigoEnBlanco_DevuelveNullSinConsultarElRepositorio()
    {
        (await RunAsync(CompanyId, "  ")).Should().BeNull();

        await _repo.DidNotReceive().FindByTagSerialAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConsultaSiempreEnLaEmpresaPedida_YNoResuelveSociosDeOtra()
    {
        var member = NewMember("30123456");
        member.EnrollTag("CARNET-77");
        _repo.FindByTagSerialAsync("CARNET-77", CompanyId, Arg.Any<CancellationToken>()).Returns(member);

        var fromOtherCompany = await RunAsync(OtherCompanyId, "CARNET-77");

        fromOtherCompany.Should().BeNull();
        await _repo.Received(1).FindByTagSerialAsync("CARNET-77", OtherCompanyId, Arg.Any<CancellationToken>());
        await _repo.DidNotReceive().FindByTagSerialAsync("CARNET-77", CompanyId, Arg.Any<CancellationToken>());
    }
}
