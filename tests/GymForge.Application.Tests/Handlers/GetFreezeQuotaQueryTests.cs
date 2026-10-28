using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Memberships;
using GymForge.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Cupo anual de congelamiento que se muestra antes de congelar: usados, tope y restantes del año local.</summary>
public class GetFreezeQuotaQueryTests
{
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly ISiteRepository _siteRepo = Substitute.For<ISiteRepository>();
    private readonly FakeClock _clock = new(FakeClock.ArgentinaNoon(new DateOnly(2026, 3, 1)));

    private GetFreezeQuotaQueryHandler Sut() => new(
        _membershipRepo, _siteRepo, new CompanyCalendar(_clock, _siteRepo, NullLogger<CompanyCalendar>.Instance));

    private Membership MembershipOf(Company company)
    {
        var ms = Membership.Create(
            company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2025, 1, 1), new DateOnly(2027, 12, 31));
        ms.Activate();
        _membershipRepo.GetByIdAsync(ms.Id, Arg.Any<CancellationToken>()).Returns(ms);
        _siteRepo.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        return ms;
    }

    private static Company CompanyWithCap(int maxFreezeDaysPerYear)
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        company.SetFreezePolicy(maxFreezeDaysPerYear);
        return company;
    }

    private static void FreezeAndFinish(Membership ms, DateOnly from, int days)
    {
        ms.Freeze(from, from.AddDays(days), "Test", maxFreezeDaysPerYear: 365);
        ms.Unfreeze(from.AddDays(days + 1));
    }

    [Fact]
    public async Task NeverFrozen_TheWholeCapIsAvailable()
    {
        var ms = MembershipOf(CompanyWithCap(90));

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.Should().Be(new FreezeQuotaDto(new DateOnly(2026, 3, 1), 2026, MaxDays: 90, UsedDays: 0));
        quota.RemainingDays.Should().Be(90);
    }

    [Fact]
    public async Task CountsTheDaysAlreadyUsedThisYear()
    {
        var ms = MembershipOf(CompanyWithCap(90));
        FreezeAndFinish(ms, new DateOnly(2026, 1, 10), 25);

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.UsedDays.Should().Be(25);
        quota.RemainingDays.Should().Be(65);
    }

    [Fact]
    public async Task TheDaysOfAPreviousYear_DoNotCount()
    {
        var ms = MembershipOf(CompanyWithCap(90));
        FreezeAndFinish(ms, new DateOnly(2025, 10, 1), 60);

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.UsedDays.Should().Be(0);
        quota.RemainingDays.Should().Be(90);
    }

    [Fact]
    public async Task TheCapIsTheOneOfTheMembershipCompany()
    {
        var ms = MembershipOf(CompanyWithCap(30));

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.MaxDays.Should().Be(30);
        quota.RemainingDays.Should().Be(30);
    }

    [Fact]
    public async Task RemainingDays_NeverGoesBelowZero_IfTheCapWasLoweredUnderTheUsage()
    {
        var company = CompanyWithCap(90);
        var ms = MembershipOf(company);
        FreezeAndFinish(ms, new DateOnly(2026, 1, 10), 80);
        company.SetFreezePolicy(60);

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.RemainingDays.Should().Be(0);
    }

    [Fact]
    public async Task TodayAndYear_AreTheCompanyLocalOnes_NotTheUtcOnes()
    {
        var ms = MembershipOf(CompanyWithCap(90));
        FreezeAndFinish(ms, new DateOnly(2026, 11, 1), 40);
        // 22:00 del 31/12/2026 en Argentina = 01:00 UTC del 01/01/2027: con la fecha UTC se vería el cupo de 2027.
        _clock.Now = FakeClock.Argentina2200(new DateOnly(2026, 12, 31));

        var quota = await Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        quota.Today.Should().Be(new DateOnly(2026, 12, 31));
        quota.Year.Should().Be(2026);
        quota.UsedDays.Should().Be(40);
    }

    [Fact]
    public async Task UnknownMembership_Throws()
    {
        _membershipRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Membership?)null);

        var act = () => Sut().Handle(new GetFreezeQuotaQuery(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UnknownCompany_Throws()
    {
        var ms = MembershipOf(CompanyWithCap(90));
        _siteRepo.GetCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Company?)null);

        var act = () => Sut().Handle(new GetFreezeQuotaQuery(ms.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
