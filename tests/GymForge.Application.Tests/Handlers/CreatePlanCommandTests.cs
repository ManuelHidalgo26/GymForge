using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Plans;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class CreatePlanCommandTests
{
    private static CreatePlanCommand PackCommand(int? visits) =>
        new(Guid.NewGuid(), "Pack", MembershipBasis.VisitPack, 10_000m, 1, "Month", visits);

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void Validator_VisitPackSinVisitas_Falla(int? visits) =>
        new CreatePlanCommandValidator().Validate(PackCommand(visits)).IsValid.Should().BeFalse();

    [Fact]
    public void Validator_VisitPackConVisitas_Pasa() =>
        new CreatePlanCommandValidator().Validate(PackCommand(10)).IsValid.Should().BeTrue();

    [Fact]
    public void Validator_PlanPorTiempoSinVisitas_Pasa() =>
        new CreatePlanCommandValidator()
            .Validate(new CreatePlanCommand(Guid.NewGuid(), "Mensual", MembershipBasis.Renewal, 1m, 1, "Month"))
            .IsValid.Should().BeTrue();

    [Fact]
    public async Task Handle_VisitPackConVisitas_PersisteLaCantidad()
    {
        var repo = Substitute.For<IMembershipTypeRepository>();

        var dto = await new CreatePlanCommandHandler(repo).Handle(PackCommand(10), CancellationToken.None);

        dto.VisitsIncluded.Should().Be(10);
        await repo.Received(1).AddAsync(
            Arg.Is<MembershipType>(p => p.VisitsIncluded == 10), Arg.Any<CancellationToken>());
    }
}
