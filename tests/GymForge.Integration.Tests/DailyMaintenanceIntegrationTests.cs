using FluentAssertions;
using GymForge.Application;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Maintenance;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using GymForge.Infrastructure;
using GymForge.Infrastructure.Persistence;
using GymForge.Infrastructure.Repositories;
using GymForge.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

/// <summary>Mantenimiento diario (ADR-003 punto 1) sobre SQLite real: repo company-wide, flujo del día y catch-up.</summary>
public class DailyMaintenanceIntegrationTests : IAsyncLifetime
{
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly AmbientCurrentUser _user = new();
    private readonly FakeClock _clock = FakeClock.OnDay(Today);
    private readonly RecordingSender _sender = new();
    private GymForgeDbContext _db = null!;
    private AmbientTenantProvider _tenant = null!;
    private Company _company = null!;
    private Site _site = null!;
    private Site _secondSite = null!;
    private MembershipType _plan = null!;

    public async Task InitializeAsync()
    {
        _db = TestDb.CreateAudited(out _tenant, _user, _clock);
        _company = Company.Create("Gym Test", "30-12345678-9");
        _site = Site.Create(_company.Id, "Central", "Av. Test 123");
        _secondSite = Site.Create(_company.Id, "Norte", "Calle Norte 456");
        _plan = MembershipType.Create(_company.Id, "Mensual", MembershipBasis.Renewal, 35_000m, 1, "Month");
        await _db.Companies.AddAsync(_company);
        await _db.Sites.AddRangeAsync(_site, _secondSite);
        await _db.MembershipTypes.AddAsync(_plan);
        await _db.SaveChangesAsync();
        // Sin tenant fijado a propósito: el mantenimiento no puede depender del tenant ambiente.
    }

    public async Task DisposeAsync()
    {
        await _db.Database.CloseConnectionAsync();
        await _db.DisposeAsync();
    }

    private DailyMaintenanceService NewService(bool dunningEnabled = false) => new(
        new MembershipRepository(_db), new ChargeRepository(_db), new MemberRepository(_db),
        new DunningService(
            new ChargeRepository(_db), new MemberRepository(_db), _sender, new NotificationLogRepository(_db), _tenant,
            new DunningConfig { Enabled = dunningEnabled }),
        _tenant);

    private async Task<Member> AddMemberAsync(Site site, string dni = "30111222", string? mobile = null)
    {
        var member = Member.Create(_company.Id, site.Id, "Ana", "García", DocumentType.DNI, dni, Gender.Female);
        member.Activate(new DateOnly(2026, 1, 1));
        if (mobile is not null) member.UpdateContact(null, mobile);
        await _db.Members.AddAsync(member);
        await _db.SaveChangesAsync();
        return member;
    }

    private async Task<Membership> AddMembershipAsync(
        Member member, DateOnly? endDate, Action<Membership>? configure = null)
    {
        var ms = Membership.Create(_company.Id, member.SiteId, member.Id, _plan.Id, Today.AddMonths(-2), endDate);
        ms.Activate();
        configure?.Invoke(ms);
        await _db.Memberships.AddAsync(ms);
        await _db.SaveChangesAsync();
        return ms;
    }

    private const int FreezeDays = 10;

    /// <summary>Congela <see cref="FreezeDays"/> días y deja el fin del congelamiento en <paramref name="freezeEnd"/>.</summary>
    private static void FreezeUntil(Membership membership, DateOnly freezeEnd) =>
        membership.Freeze(freezeEnd.AddDays(-FreezeDays), freezeEnd, "Viaje");

    private async Task<Charge> AddChargeAsync(Member member, int daysOverdue, decimal amount = 10_000m)
    {
        var charge = Charge.Create(_company.Id, member.SiteId, member.Id, null,
            ConceptType.MembershipFee, "Cuota", amount, Today.AddDays(-daysOverdue));
        await _db.Charges.AddAsync(charge);
        await _db.SaveChangesAsync();
        return charge;
    }

    private async Task<T> ReloadAsync<T>(T entity) where T : BaseEntity
    {
        _db.ChangeTracker.Clear();
        using (_tenant.BeginScope(_company.Id, ((ISiteScoped)entity).SiteId))
            return await _db.Set<T>().SingleAsync(e => e.Id == entity.Id);
    }

    // ── GetActiveExpiredAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task GetActiveExpired_ReturnsOnlyActiveExpiredMemberships_OfTheWholeCompany()
    {
        var anaCentral = await AddMemberAsync(_site, "30111222");
        var anaNorte = await AddMemberAsync(_secondSite, "30111333");
        var expiredCentral = await AddMembershipAsync(anaCentral, Today.AddDays(-1));
        var expiredNorte = await AddMembershipAsync(anaNorte, Today.AddDays(-10));
        await AddMembershipAsync(anaCentral, Today);                                    // vence hoy: todavía vale
        await AddMembershipAsync(anaCentral, Today.AddDays(5));
        await AddMembershipAsync(anaCentral, null);                                     // sin vencimiento
        await AddMembershipAsync(anaCentral, Today.AddDays(-3), m => m.Cancel("Baja"));
        await AddMembershipAsync(anaCentral, Today.AddDays(-3), m => m.Expire());       // ya vencida
        await AddMembershipAsync(anaCentral, Today.AddDays(40),
            m => m.Freeze(Today.AddDays(-2), Today.AddDays(20), "Viaje"));

        var otherCompany = Company.Create("Otro Gym", "30-98765432-1");
        var otherSite = Site.Create(otherCompany.Id, "Otra", "Calle 1");
        var otherMember = Member.Create(otherCompany.Id, otherSite.Id, "Zoe", "Paz", DocumentType.DNI, "40111222", Gender.Female);
        var otherPlan = MembershipType.Create(otherCompany.Id, "Mensual", MembershipBasis.Renewal, 20_000m, 1, "Month");
        var otherMembership = Membership.Create(
            otherCompany.Id, otherSite.Id, otherMember.Id, otherPlan.Id, Today.AddMonths(-2), Today.AddDays(-1));
        otherMembership.Activate();
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _db.Members.AddAsync(otherMember);
        await _db.MembershipTypes.AddAsync(otherPlan);
        await _db.Memberships.AddAsync(otherMembership);
        await _db.SaveChangesAsync();

        _tenant.Set(otherCompany.Id, otherSite.Id);   // aunque el tenant ambiente sea otro, manda el CompanyId
        var found = await new MembershipRepository(_db).GetActiveExpiredAsync(_company.Id, Today);

        found.Select(m => m.Id).Should().BeEquivalentTo(new[] { expiredCentral.Id, expiredNorte.Id });
    }

    // ── Reactivar congelamientos terminados ───────────────────────────────────

    [Fact]
    public async Task GetFreezeEnded_ReturnsOnlyTheFrozenWhoseFreezeEnded_OfTheWholeCompany()
    {
        var anaCentral = await AddMemberAsync(_site, "30111222");
        var anaNorte = await AddMemberAsync(_secondSite, "30111333");
        var endsToday = await AddMembershipAsync(anaCentral, Today.AddDays(60), m => FreezeUntil(m, Today));
        var endedLongAgo = await AddMembershipAsync(anaNorte, Today.AddDays(60), m => FreezeUntil(m, Today.AddDays(-9)));
        await AddMembershipAsync(anaCentral, Today.AddDays(60), m => FreezeUntil(m, Today.AddDays(1)));   // termina mañana
        await AddMembershipAsync(anaCentral, Today.AddDays(60));                                          // nunca se congeló
        await AddMembershipAsync(anaCentral, Today.AddDays(60), m =>
        {
            FreezeUntil(m, Today.AddDays(-2));
            m.Cancel("Baja durante el freeze");
        });

        var otherCompany = Company.Create("Otro Gym", "30-98765432-1");
        var otherSite = Site.Create(otherCompany.Id, "Otra", "Calle 1");
        var otherMember = Member.Create(otherCompany.Id, otherSite.Id, "Zoe", "Paz", DocumentType.DNI, "40111222", Gender.Female);
        var otherPlan = MembershipType.Create(otherCompany.Id, "Mensual", MembershipBasis.Renewal, 20_000m, 1, "Month");
        var otherMembership = Membership.Create(
            otherCompany.Id, otherSite.Id, otherMember.Id, otherPlan.Id, Today.AddMonths(-2), Today.AddDays(60));
        otherMembership.Activate();
        FreezeUntil(otherMembership, Today);
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _db.Members.AddAsync(otherMember);
        await _db.MembershipTypes.AddAsync(otherPlan);
        await _db.Memberships.AddAsync(otherMembership);
        await _db.SaveChangesAsync();

        _tenant.Set(otherCompany.Id, otherSite.Id);   // aunque el tenant ambiente sea otro, manda el CompanyId
        var found = await new MembershipRepository(_db).GetFreezeEndedAsync(_company.Id, Today);

        found.Select(m => m.Id).Should().BeEquivalentTo(new[] { endsToday.Id, endedLongAgo.Id });
    }

    [Fact]
    public async Task MaintenanceDay_ThawsTheMembership_AndKeepsItsExtendedEndDate()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today));
        var endDateWhileFrozen = frozen.EndDate;

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.Unfrozen.Should().Be(1);
        var reloaded = await ReloadAsync(frozen);
        reloaded.Status.Should().Be(MembershipStatus.Active);
        reloaded.FreezeStart.Should().BeNull();
        reloaded.FreezeEnd.Should().BeNull();
        reloaded.EndDate.Should().Be(endDateWhileFrozen);
        reloaded.FreezeDaysUsedYear.Should().Be(FreezeDays);
    }

    [Fact]
    public async Task MaintenanceDay_ThawedMembership_IsNotExpiredTheSameDay()
    {
        var member = await AddMemberAsync(_site);
        // Habría vencido hace 5 días, pero los 10 días de freeze corrieron su vencimiento a 5 días en el futuro.
        var frozen = await AddMembershipAsync(member, Today.AddDays(-5), m => FreezeUntil(m, Today));

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.Should().Be(default(MaintenanceDayResult) with { Unfrozen = 1 });
        (await ReloadAsync(frozen)).Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task MaintenanceDay_FreezeStillRunning_StaysFrozen()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today.AddDays(1)));

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.Unfrozen.Should().Be(0);
        (await ReloadAsync(frozen)).Status.Should().Be(MembershipStatus.Frozen);
    }

    [Fact]
    public async Task MaintenanceDay_Thaw_IsIdempotent_AndDoesNotRewriteWhatDidNotChange()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today));
        await NewService().RunForCompanyDayAsync(_company, Today);
        var afterFirstRun = await ReloadAsync(frozen);

        var second = await NewService().RunForCompanyDayAsync(_company, Today);

        second.Should().Be(default(MaintenanceDayResult));
        var afterSecondRun = await ReloadAsync(frozen);
        afterSecondRun.Status.Should().Be(MembershipStatus.Active);
        afterSecondRun.UpdatedAt.Should().Be(afterFirstRun.UpdatedAt);
        afterSecondRun.FreezeDaysUsedYear.Should().Be(FreezeDays, "reprocesar el día no devuelve ni cobra cupo");
    }

    [Fact]
    public async Task MaintenanceDay_Thaw_IsAuditedAsASystemOperation()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today));
        _user.Set(null);

        await NewService().RunForCompanyDayAsync(_company, Today);

        var row = await _db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EntityType == nameof(Membership) && a.EntityId == frozen.Id && a.Action == "Update");
        row.UserId.Should().BeNull();
        row.DiffJson.Should().Contain("""{"old":"Frozen","new":"Active"}""");
    }

    [Fact]
    public async Task Runner_ThawsOnTheLocalDay_NotOnTheUtcOne()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today));
        var runner = NewRunner(new InMemoryMaintenanceCheckpoint(), dunningEnabled: false);

        // 22:00 del día anterior en Argentina = 01:00 UTC de hoy: con la fecha UTC se reactivaría un día antes.
        _clock.Now = FakeClock.At2200Argentina(Today.AddDays(-1)).Now;
        await runner.RunPendingAsync();
        (await ReloadAsync(frozen)).Status.Should().Be(MembershipStatus.Frozen);

        _clock.Now = FakeClock.OnDay(Today).Now;
        await runner.RunPendingAsync();
        (await ReloadAsync(frozen)).Status.Should().Be(MembershipStatus.Active);
    }

    [Fact]
    public async Task Runner_CatchUp_ThawsAFreezeThatEndedWhileTheAppWasClosed()
    {
        var member = await AddMemberAsync(_site);
        var frozen = await AddMembershipAsync(member, Today.AddDays(60), m => FreezeUntil(m, Today.AddDays(-2)));
        var checkpoint = new InMemoryMaintenanceCheckpoint { LastRun = Today.AddDays(-4) };

        await NewRunner(checkpoint, dunningEnabled: false).RunPendingAsync();

        (await ReloadAsync(frozen)).Status.Should().Be(MembershipStatus.Active);
        checkpoint.LastRun.Should().Be(Today);
    }

    // ── Flujo del día ─────────────────────────────────────────────────────────

    [Fact]
    public async Task MaintenanceDay_ExpiresMembership_MarksChargeAndMemberOverdue()
    {
        var member = await AddMemberAsync(_site);
        var membership = await AddMembershipAsync(member, Today.AddDays(-1));
        var charge = await AddChargeAsync(member, daysOverdue: 5);

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.Should().Be(new MaintenanceDayResult(Unfrozen: 0, Expired: 1, MarkedOverdue: 1, DunningSent: 0, ArrearsCleared: 0));
        (await ReloadAsync(membership)).Status.Should().Be(MembershipStatus.Expired);
        (await ReloadAsync(charge)).Status.Should().Be(ChargeStatus.Overdue);
        (await ReloadAsync(member)).Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task MaintenanceDay_CoversEverySiteOfTheCompany()
    {
        var central = await AddMemberAsync(_site, "30111222");
        var norte = await AddMemberAsync(_secondSite, "30111333");
        var chargeCentral = await AddChargeAsync(central, 4);
        var chargeNorte = await AddChargeAsync(norte, 4);

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.MarkedOverdue.Should().Be(2);
        (await ReloadAsync(chargeCentral)).Status.Should().Be(ChargeStatus.Overdue);
        (await ReloadAsync(chargeNorte)).Status.Should().Be(ChargeStatus.Overdue);
    }

    [Fact]
    public async Task MaintenanceDay_IsIdempotent_AndDoesNotRewriteWhatDidNotChange()
    {
        var member = await AddMemberAsync(_site);
        var charge = await AddChargeAsync(member, 5);
        await NewService().RunForCompanyDayAsync(_company, Today);
        var chargeAfterFirstRun = await ReloadAsync(charge);
        var memberAfterFirstRun = await ReloadAsync(member);

        var second = await NewService().RunForCompanyDayAsync(_company, Today);

        second.Should().Be(default(MaintenanceDayResult));
        (await ReloadAsync(charge)).UpdatedAt.Should().Be(chargeAfterFirstRun.UpdatedAt);
        (await ReloadAsync(member)).UpdatedAt.Should().Be(memberAfterFirstRun.UpdatedAt);
    }

    [Fact]
    public async Task MaintenanceDay_LeavesSettledAndFutureChargesAlone()
    {
        var member = await AddMemberAsync(_site);
        var settled = await AddChargeAsync(member, 5);
        settled.ApplyPayment(10_000m);
        var notDueYet = await AddChargeAsync(member, -3);
        await _db.SaveChangesAsync();

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.MarkedOverdue.Should().Be(0);
        (await ReloadAsync(settled)).Status.Should().Be(ChargeStatus.Paid);
        (await ReloadAsync(notDueYet)).Status.Should().Be(ChargeStatus.Pending);
        (await ReloadAsync(member)).Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task MaintenanceDay_IsAuditedAsASystemOperation()
    {
        var member = await AddMemberAsync(_site);
        var membership = await AddMembershipAsync(member, Today.AddDays(-1));
        _user.Set(null);

        await NewService().RunForCompanyDayAsync(_company, Today);

        var row = await _db.AuditLogs.IgnoreQueryFilters()
            .SingleAsync(a => a.EntityType == nameof(Membership) && a.Action == "Update");
        row.EntityId.Should().Be(membership.Id);
        row.UserId.Should().BeNull();
        row.DiffJson.Should().Be("""{"Status":{"old":"Active","new":"Expired"}}""");
    }

    // ── Limpiar la mora saldada (4º paso) ─────────────────────────────────────

    private async Task<Member> AddMemberThenAsync(string dni, Action<Member> transition, Site? site = null)
    {
        var member = await AddMemberAsync(site ?? _site, dni);
        transition(member);
        await _db.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task MaintenanceDay_ReturnsToActiveTheMemberWhoseDebtWasSettled()
    {
        var member = await AddMemberThenAsync("30111222", m => m.MarkOverdue());
        var charge = await AddChargeAsync(member, daysOverdue: 5);
        charge.ApplyPayment(10_000m);
        await _db.SaveChangesAsync();

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.ArrearsCleared.Should().Be(1);
        (await ReloadAsync(member)).Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task MaintenanceDay_KeepsOverdueTheMemberWhoStillOwes()
    {
        var member = await AddMemberThenAsync("30111222", m => m.MarkOverdue());
        await AddChargeAsync(member, daysOverdue: 5);

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.ArrearsCleared.Should().Be(0);
        (await ReloadAsync(member)).Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task MaintenanceDay_PartialPaymentKeepsTheMemberInArrears()
    {
        var member = await AddMemberThenAsync("30111222", m => m.MarkOverdue());
        var charge = await AddChargeAsync(member, daysOverdue: 5);
        charge.ApplyPayment(4_000m);
        await _db.SaveChangesAsync();

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.ArrearsCleared.Should().Be(0);
        (await ReloadAsync(member)).Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task MaintenanceDay_ClearsEverySite_ButNeverTouchesAnotherCompany()
    {
        var central = await AddMemberThenAsync("30111222", m => m.MarkOverdue());
        var norte = await AddMemberThenAsync("30111333", m => m.MarkOverdue(), _secondSite);

        var otherCompany = Company.Create("Otro Gym", "30-98765432-1");
        var otherSite = Site.Create(otherCompany.Id, "Otra", "Calle 1");
        var stranger = Member.Create(otherCompany.Id, otherSite.Id, "Zoe", "Paz", DocumentType.DNI, "40111222", Gender.Female);
        stranger.Activate(new DateOnly(2026, 1, 1));
        stranger.MarkOverdue();
        await _db.Companies.AddAsync(otherCompany);
        await _db.Sites.AddAsync(otherSite);
        await _db.Members.AddAsync(stranger);
        await _db.SaveChangesAsync();

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.ArrearsCleared.Should().Be(2);
        (await ReloadAsync(central)).Status.Should().Be(MemberStatus.Active);
        (await ReloadAsync(norte)).Status.Should().Be(MemberStatus.Active);
        _db.ChangeTracker.Clear();
        using (_tenant.BeginScope(otherCompany.Id, otherSite.Id))
            (await _db.Members.SingleAsync(m => m.Id == stranger.Id)).Status.Should().Be(MemberStatus.Overdue);
    }

    [Fact]
    public async Task MaintenanceDay_ClearingLeavesEveryOtherStatusAlone()
    {
        var prospect = Member.Create(_company.Id, _site.Id, "Pablo", "Nuevo", DocumentType.DNI, "30111301", Gender.Male);
        await _db.Members.AddAsync(prospect);
        await _db.SaveChangesAsync();
        var frozen = await AddMemberThenAsync("30111302", m => m.Freeze());
        var suspended = await AddMemberThenAsync("30111303", m => m.Suspend());
        var cancelled = await AddMemberThenAsync("30111304", m => m.Cancel());
        var active = await AddMemberThenAsync("30111305", _ => { });

        var result = await NewService().RunForCompanyDayAsync(_company, Today);

        result.ArrearsCleared.Should().Be(0);
        (await ReloadAsync(prospect)).Status.Should().Be(MemberStatus.Prospect);
        (await ReloadAsync(frozen)).Status.Should().Be(MemberStatus.Frozen);
        (await ReloadAsync(suspended)).Status.Should().Be(MemberStatus.Suspended);
        (await ReloadAsync(cancelled)).Status.Should().Be(MemberStatus.Cancelled);
        (await ReloadAsync(active)).Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task MaintenanceDay_ClearingIsIdempotent_AndDoesNotRewriteWhatDidNotChange()
    {
        var member = await AddMemberThenAsync("30111222", m => m.MarkOverdue());
        await NewService().RunForCompanyDayAsync(_company, Today);
        var afterFirstRun = await ReloadAsync(member);

        var second = await NewService().RunForCompanyDayAsync(_company, Today);

        second.Should().Be(default(MaintenanceDayResult));
        (await ReloadAsync(member)).UpdatedAt.Should().Be(afterFirstRun.UpdatedAt);
    }

    // ── Catch-up de punta a punta ─────────────────────────────────────────────

    [Fact]
    public async Task Runner_CatchUp_RecoversTheDunningStageThatFellOnAClosedDay()
    {
        var member = await AddMemberAsync(_site, mobile: "+5491155550000");
        await AddChargeAsync(member, daysOverdue: 9);   // la etapa de 7 días cayó 2 días atrás (gym cerrado)
        var checkpoint = new InMemoryMaintenanceCheckpoint { LastRun = Today.AddDays(-3) };
        var runner = NewRunner(checkpoint, dunningEnabled: true);

        await runner.RunPendingAsync();

        _sender.Messages.Should().ContainSingle();
        checkpoint.LastRun.Should().Be(Today);
    }

    [Fact]
    public async Task Runner_FirstRun_DoesNotBackfillOldDunningStages()
    {
        var member = await AddMemberAsync(_site, mobile: "+5491155550000");
        await AddChargeAsync(member, daysOverdue: 9);
        var runner = NewRunner(new InMemoryMaintenanceCheckpoint(), dunningEnabled: true);   // primer arranque: solo hoy

        await runner.RunPendingAsync();

        _sender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Runner_RunningTheSameDayTwice_DoesNotResendTheReminder()
    {
        var member = await AddMemberAsync(_site, mobile: "+5491155550000");
        await AddChargeAsync(member, daysOverdue: 7);
        var runner = NewRunner(new InMemoryMaintenanceCheckpoint(), dunningEnabled: true);

        await runner.RunPendingAsync();
        await runner.RunPendingAsync();

        _sender.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task MaintenanceDay_ReprocessingTheSameDay_DoesNotResendTheReminder_AndLogsItOnce()
    {
        // El checkpoint puede obligar a repetir un día: el NotificationLog evita el aviso duplicado.
        var member = await AddMemberAsync(_site, mobile: "+5491155550000");
        var charge = await AddChargeAsync(member, daysOverdue: 7);

        var first = await NewService(dunningEnabled: true).RunForCompanyDayAsync(_company, Today);
        var second = await NewService(dunningEnabled: true).RunForCompanyDayAsync(_company, Today);

        first.DunningSent.Should().Be(1);
        second.DunningSent.Should().Be(0);
        _sender.Messages.Should().ContainSingle();

        _db.ChangeTracker.Clear();
        var log = await _db.NotificationLogs.IgnoreQueryFilters().SingleAsync();
        log.ChargeId.Should().Be(charge.Id);
        log.SiteId.Should().Be(_site.Id);
        log.StageDaysOverdue.Should().Be(7);
        log.Trigger.Should().Be(NotificationTrigger.Automatic);
    }

    private DailyMaintenanceRunner NewRunner(IMaintenanceCheckpoint checkpoint, bool dunningEnabled) => new(
        new SiteRepository(_db), NewService(dunningEnabled), checkpoint, TestServices.Calendar(_db, _clock),
        NullLogger<DailyMaintenanceRunner>.Instance);

    // ── Wiring por DI ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Di_ResolvesTheRunner_AndItWorksEndToEnd()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_maintenance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var checkpoint = new InMemoryMaintenanceCheckpoint();

        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddApplication();
            services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
            services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
            services.AddInfrastructure($"Data Source={Path.Combine(dir, "gymforge.db")}");
            services.AddSingleton<IClock>(_clock);
            services.AddSingleton<IMaintenanceCheckpoint>(checkpoint);   // no tocar el archivo real del usuario
            await using var provider = services.BuildServiceProvider();

            Guid chargeId;
            using (var seedScope = provider.CreateScope())
            {
                var db = seedScope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
                await db.Database.MigrateAsync();
                var company = Company.Create("Gym DI", "30-55555555-5");
                var site = Site.Create(company.Id, "Central", "Av. DI 1");
                var member = Member.Create(company.Id, site.Id, "Luz", "Díaz", DocumentType.DNI, "30999888", Gender.Female);
                member.Activate(new DateOnly(2026, 1, 1));
                var charge = Charge.Create(company.Id, site.Id, member.Id, null,
                    ConceptType.MembershipFee, "Cuota", 10_000m, Today.AddDays(-5));
                await db.Companies.AddAsync(company);
                await db.Sites.AddAsync(site);
                await db.Members.AddAsync(member);
                await db.Charges.AddAsync(charge);
                await db.SaveChangesAsync();
                chargeId = charge.Id;
            }

            using (var runScope = provider.CreateScope())
                await runScope.ServiceProvider.GetRequiredService<IDailyMaintenanceRunner>().RunPendingAsync();

            using var checkScope = provider.CreateScope();
            var checkDb = checkScope.ServiceProvider.GetRequiredService<GymForgeDbContext>();
            (await checkDb.Charges.IgnoreQueryFilters().SingleAsync(c => c.Id == chargeId))
                .Status.Should().Be(ChargeStatus.Overdue);
            checkpoint.LastRun.Should().Be(Today);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Di_DefaultCheckpoint_IsTheSingletonFileOne()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ICurrentTenant, AmbientTenantProvider>();
        services.AddSingleton<ICurrentUser, AmbientCurrentUser>();
        services.AddInfrastructure("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();

        var first = provider.GetRequiredService<IMaintenanceCheckpoint>();

        first.Should().BeOfType<FileMaintenanceCheckpoint>();
        provider.GetRequiredService<IMaintenanceCheckpoint>().Should().BeSameAs(first);
    }

    // ── Dobles de prueba ──────────────────────────────────────────────────────

    private sealed class RecordingSender : INotificationSender
    {
        public List<NotificationMessage> Messages { get; } = [];

        public Task<bool> SendAsync(NotificationMessage message, CancellationToken ct = default)
        {
            Messages.Add(message);
            return Task.FromResult(true);
        }
    }

    private sealed class InMemoryMaintenanceCheckpoint : IMaintenanceCheckpoint
    {
        public DateOnly? LastRun { get; set; }

        public DateOnly? ReadLastRun() => LastRun;

        public void WriteLastRun(DateOnly localDay) => LastRun = localDay;
    }
}
