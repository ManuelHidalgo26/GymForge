using FluentAssertions;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// La pantalla de morosos y el recordatorio por WhatsApp contra SQLite real y los handlers de verdad (no fakes):
/// un cajero de la sede A no ve ni puede avisar a los morosos de la sede B (deuda D1, evitada en la UI nueva).
/// Mundo: una empresa con sedes Centro y Norte, un moroso en cada una.
/// </summary>
public class CollectionsTenantIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _centro = null!, _norte = null!;
    private Member _ana = null!, _beto = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.Create(out _tenant);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _centro = Site.Create(_company.Id, "Centro", "Calle 1");
        _norte = Site.Create(_company.Id, "Norte", "Calle 2");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddRangeAsync(_centro, _norte);

        _ana = await AddDebtorAsync(_centro, "Ana", "40000001", daysOverdue: 10);
        _beto = await AddDebtorAsync(_norte, "Beto", "40000002", daysOverdue: 20);
        await _db.SaveChangesAsync();

        _tenant.Set(_company.Id, _centro.Id);   // el cajero está en Centro
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private async Task<Member> AddDebtorAsync(Site site, string firstName, string dni, int daysOverdue)
    {
        var member = Member.Create(_company.Id, site.Id, firstName, "Test", DocumentType.DNI, dni, Gender.Female);
        member.UpdateContact($"{firstName.ToLowerInvariant()}@test.com", "11 5555-0000");
        await _db.Members.AddAsync(member);
        await _db.Charges.AddAsync(Charge.Create(
            _company.Id, site.Id, member.Id, null, ConceptType.MembershipFee, "Cuota", 35_000m, Today.AddDays(-daysOverdue)));
        return member;
    }

    private GetOverdueMembersQueryHandler Query() =>
        new(new ChargeRepository(_db), new NotificationLogRepository(_db), new DunningConfig());

    private SendWhatsAppReminderCommandHandler Reminder()
    {
        var calendar = Substitute.For<ICompanyCalendar>();
        calendar.TodayFor(Arg.Any<Company>()).Returns(Today);
        return new SendWhatsAppReminderCommandHandler(
            new MemberRepository(_db), new ChargeRepository(_db), new SiteRepository(_db),
            calendar, new NotificationLogRepository(_db), new DunningConfig());
    }

    private Task<int> LogCountAsync() => _db.NotificationLogs.IgnoreQueryFilters().CountAsync();

    [Fact]
    public async Task TheList_OnlyShowsTheDebtorsOfTheAskedSite()
    {
        var centro = await Query().Handle(new GetOverdueMembersQuery(_company.Id, _centro.Id, Today), default);
        var norte = await Query().Handle(new GetOverdueMembersQuery(_company.Id, _norte.Id, Today), default);

        centro.Select(r => r.MemberId).Should().Equal(_ana.Id);
        norte.Select(r => r.MemberId).Should().Equal(_beto.Id);
    }

    [Fact]
    public async Task ACashierAtCentro_CannotRemindAMemberOfNorte_NorLeaveATrace()
    {
        var act = () => Reminder().Handle(new SendWhatsAppReminderCommand(_company.Id, _norte.Id, _beto.Id), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await LogCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ASpoofedSiteInTheCommand_DoesNotOpenTheOtherSite_NorWritesALog()
    {
        // Pedir el socio de Norte diciendo que se está en Centro: el socio no se ve (filtro) y la sede no coincide.
        var act = () => Reminder().Handle(new SendWhatsAppReminderCommand(_company.Id, _centro.Id, _beto.Id), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await LogCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RemindingAMemberOfTheOwnSite_WritesAManualLogInThatSite_AndTheListShowsIt()
    {
        var result = await Reminder().Handle(new SendWhatsAppReminderCommand(_company.Id, _centro.Id, _ana.Id), default);

        result.PhoneUsable.Should().BeTrue();
        result.WhatsAppUrl.Should().StartWith("https://wa.me/5491155550000?text=");

        _db.ChangeTracker.Clear();
        var log = await _db.NotificationLogs.IgnoreQueryFilters().SingleAsync();
        log.SiteId.Should().Be(_centro.Id);
        log.MemberId.Should().Be(_ana.Id);
        log.Trigger.Should().Be(NotificationTrigger.Manual);

        var row = (await Query().Handle(new GetOverdueMembersQuery(_company.Id, _centro.Id, Today), default)).Single();
        row.LastReminderAt.Should().NotBeNull();
    }

    [Fact]
    public async Task TheLastReminder_OfADebtorOfAnotherSite_IsNotLeakedIntoTheCurrentSiteList()
    {
        // Norte avisó a Beto; parados en Centro, pedir la lista de Norte no revela ese aviso (el log es por sede).
        using (_tenant.BeginScope(_company.Id, _norte.Id))
            await Reminder().Handle(new SendWhatsAppReminderCommand(_company.Id, _norte.Id, _beto.Id), default);

        var norteSeenFromCentro = await Query().Handle(new GetOverdueMembersQuery(_company.Id, _norte.Id, Today), default);

        norteSeenFromCentro.Single().LastReminderAt.Should().BeNull("el tenant activo es Centro");
    }
}
