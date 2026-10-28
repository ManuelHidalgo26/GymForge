using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Memberships;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class CreateMembershipCommandTests
{
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly IMembershipTypeRepository _planRepo = Substitute.For<IMembershipTypeRepository>();

    private CreateMembershipCommandHandler Sut() => new(_memberRepo, _membershipRepo, _planRepo);

    private static Member MakeMember(Guid companyId, Guid siteId) =>
        Member.Create(companyId, siteId, "Juan", "Pérez", DocumentType.DNI, "30111222", Gender.Male);

    [Fact]
    public async Task Handle_MonthlyPlan_SetsEndDateFromPlanDuration()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var start = new DateOnly(2026, 3, 15);
        var plan = MembershipType.Create(companyId, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        var member = MakeMember(companyId, siteId);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);

        var dto = await Sut().Handle(
            new CreateMembershipCommand(companyId, siteId, member.Id, plan.Id, start),
            CancellationToken.None);

        dto.EndDate.Should().Be(start.AddMonths(1));
        await _membershipRepo.Received(1).AddAsync(
            Arg.Is<Membership>(m => m.EndDate == start.AddMonths(1)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownPlan_ThrowsAndPersistsNothing()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var member = MakeMember(companyId, siteId);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _planRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((MembershipType?)null);

        var act = () => Sut().Handle(
            new CreateMembershipCommand(companyId, siteId, member.Id, Guid.NewGuid(), new DateOnly(2026, 3, 15)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _membershipRepo.DidNotReceive().AddAsync(Arg.Any<Membership>(), Arg.Any<CancellationToken>());
    }
}
