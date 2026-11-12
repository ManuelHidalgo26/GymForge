using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
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
    public async Task Handle_VisitPack_SiembraLasVisitasDelPlan()
    {
        var membership = await HandleCapturingMembershipAsync(MembershipBasis.VisitPack, visits: 10);

        membership.VisitsRemaining.Should().Be(10);
    }

    [Fact]
    public async Task Handle_PlanPorTiempo_NoTieneVisitas()
    {
        var membership = await HandleCapturingMembershipAsync(MembershipBasis.Renewal, visits: null);

        membership.VisitsRemaining.Should().BeNull();
    }

    private async Task<Membership> HandleCapturingMembershipAsync(MembershipBasis basis, int? visits)
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var plan = MembershipType.Create(companyId, "Plan", basis, 10_000m, visitsIncluded: visits);
        var member = MakeMember(companyId, siteId);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);
        Membership? created = null;
        await _membershipRepo.AddAsync(Arg.Do<Membership>(m => created = m), Arg.Any<CancellationToken>());

        await Sut().Handle(
            new CreateMembershipCommand(companyId, siteId, member.Id, plan.Id, new DateOnly(2026, 3, 15)),
            CancellationToken.None);

        return created!;
    }

    /// <summary>Un plan VisitPack legado: creado antes de que el dominio exigiera las visitas (VisitsIncluded = null).</summary>
    private static MembershipType LegacyVisitPackWithoutVisits(Guid companyId)
    {
        var plan = MembershipType.Create(companyId, "Pack", MembershipBasis.VisitPack, 10_000m, visitsIncluded: 1);
        typeof(MembershipType).GetProperty(nameof(MembershipType.VisitsIncluded))!.SetValue(plan, null);
        return plan;
    }

    [Fact]
    public async Task Handle_VisitPackLegadoSinVisitas_AbortaYNoPersisteNada()
    {
        var companyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var plan = LegacyVisitPackWithoutVisits(companyId);
        var member = MakeMember(companyId, siteId);
        _memberRepo.GetByIdAsync(member.Id, Arg.Any<CancellationToken>()).Returns(member);
        _planRepo.GetByIdAsync(plan.Id, Arg.Any<CancellationToken>()).Returns(plan);

        var act = () => Sut().Handle(
            new CreateMembershipCommand(companyId, siteId, member.Id, plan.Id, new DateOnly(2026, 3, 15)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _membershipRepo.DidNotReceive().AddAsync(Arg.Any<Membership>(), Arg.Any<CancellationToken>());
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

public class CreateMembershipCommandValidatorTests
{
    private static readonly DateOnly Today = new(2026, 7, 21);
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();

    private CreateMembershipCommandValidator Sut()
    {
        _calendar.TodayAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Today);
        return new CreateMembershipCommandValidator(_calendar);
    }

    private static CreateMembershipCommand Command(DateOnly start) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), start);

    [Fact]
    public async Task Validate_InicioAyerDelGimnasio_EsValido()
    {
        var result = await Sut().ValidateAsync(Command(Today.AddDays(-1)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_InicioAnteriorAAyerDelGimnasio_EsInvalido()
    {
        var result = await Sut().ValidateAsync(Command(Today.AddDays(-2)));

        result.IsValid.Should().BeFalse();
    }
}
