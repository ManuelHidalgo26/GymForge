using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Memberships;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class FreezeMembershipCommandTests
{
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly ISiteRepository _siteRepo = Substitute.For<ISiteRepository>();
    private readonly FakeClock _clock = new(FakeClock.ArgentinaNoon(new DateOnly(2026, 3, 1)));

    private FreezeMembershipCommandHandler FreezeSut() => new(
        _membershipRepo, _siteRepo, new CompanyCalendar(_clock, _siteRepo, NullLogger<CompanyCalendar>.Instance));

    private UnfreezeMembershipCommandHandler UnfreezeSut() => new(
        _membershipRepo, new CompanyCalendar(_clock, _siteRepo, NullLogger<CompanyCalendar>.Instance));

    private static Company CompanyWithCap(int maxFreezeDaysPerYear)
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        company.SetFreezePolicy(maxFreezeDaysPerYear);
        return company;
    }

    private Membership ActiveMembershipOf(Company company)
    {
        var ms = Membership.Create(
            company.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        ms.Activate();
        _membershipRepo.GetByIdAsync(ms.Id, Arg.Any<CancellationToken>()).Returns(ms);
        _siteRepo.GetCompanyAsync(company.Id, Arg.Any<CancellationToken>()).Returns(company);
        return ms;
    }

    // ── Freeze ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Freeze_UsesTheCapOfTheMembershipCompany()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(30));
        var cmd = new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 4, 1), "Viaje");

        var act = () => FreezeSut().Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*30*");
        await _membershipRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_WithinTheCap_FreezesAndPersists()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(30));
        var cmd = new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje");

        var dto = await FreezeSut().Handle(cmd, CancellationToken.None);

        dto.Status.Should().Be(MembershipStatus.Frozen);
        _membershipRepo.Received(1).Update(ms);
        await _membershipRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_StartInAFutureDate_IsRejectedAndNothingChanges()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        var cmd = new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 20), "Viaje");

        var act = () => FreezeSut().Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*año en curso*");
        ms.Status.Should().Be(MembershipStatus.Active);
        await _membershipRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_StartInAnotherYear_IsRejected_SoItCannotOverwriteTheYearlyCount()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        var cmd = new FreezeMembershipCommand(ms.Id, new DateOnly(2025, 12, 20), new DateOnly(2026, 1, 10), "Viaje");

        var act = () => FreezeSut().Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*año en curso*");
        await _membershipRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_UnknownMembership_Throws()
    {
        _membershipRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Membership?)null);
        var cmd = new FreezeMembershipCommand(Guid.NewGuid(), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 11), "x");

        var act = () => FreezeSut().Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Freeze_UnknownCompany_Throws()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        _siteRepo.GetCompanyAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Company?)null);
        var cmd = new FreezeMembershipCommand(ms.Id, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 11), "x");

        var act = () => FreezeSut().Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _membershipRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ── Unfreeze ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unfreeze_Reactivates_AndPersists()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje");
        _clock.Now = FakeClock.ArgentinaNoon(new DateOnly(2026, 4, 5));

        var dto = await UnfreezeSut().Handle(new UnfreezeMembershipCommand(ms.Id), CancellationToken.None);

        dto.Status.Should().Be(MembershipStatus.Active);
        dto.FreezeStart.Should().BeNull();
        _membershipRepo.Received(1).Update(ms);
        await _membershipRepo.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unfreeze_BeforeFreezeEnd_GivesBackTheUnusedDays()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje");   // fin 2026-12-31 → 2027-01-30
        _clock.Now = FakeClock.ArgentinaNoon(new DateOnly(2026, 3, 11));

        var dto = await UnfreezeSut().Handle(new UnfreezeMembershipCommand(ms.Id), CancellationToken.None);

        dto.EndDate.Should().Be(new DateOnly(2027, 1, 10));
    }

    [Fact]
    public async Task Unfreeze_At2200Argentina_CountsTheLocalDay_NotTheUtcOne()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));
        ms.Freeze(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), "Viaje");   // fin 2026-12-31 → 2027-01-30
        // 22:00 del 11/03 en Argentina = 01:00 UTC del 12/03: la fecha UTC devolvería un día de menos.
        _clock.Now = FakeClock.Argentina2200(new DateOnly(2026, 3, 11));

        var dto = await UnfreezeSut().Handle(new UnfreezeMembershipCommand(ms.Id), CancellationToken.None);

        dto.EndDate.Should().Be(new DateOnly(2027, 1, 10), "se devuelven 20 días (11→31/03), no 19");
    }

    [Fact]
    public async Task Unfreeze_UnknownMembership_Throws()
    {
        _membershipRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Membership?)null);

        var act = () => UnfreezeSut().Handle(new UnfreezeMembershipCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Unfreeze_NotFrozen_ThrowsAndPersistsNothing()
    {
        var ms = ActiveMembershipOf(CompanyWithCap(90));

        var act = () => UnfreezeSut().Handle(new UnfreezeMembershipCommand(ms.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _membershipRepo.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
