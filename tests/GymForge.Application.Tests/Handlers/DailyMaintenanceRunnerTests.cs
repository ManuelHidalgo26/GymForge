using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Maintenance;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Application.Tests.Handlers;

/// <summary>Catch-up del mantenimiento diario: ventana, idempotencia, huso horario y tolerancia a fallas.</summary>
public class DailyMaintenanceRunnerTests
{
    private const string ArgentinaTz = "Argentina Standard Time";

    // 15:00 UTC = 12:00 en Argentina: el día local y el UTC coinciden.
    private static readonly DateTime NoonArgentina = new(2026, 7, 21, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 7, 21);

    private readonly ISiteRepository _sites = Substitute.For<ISiteRepository>();
    private readonly IMembershipRepository _memberships = Substitute.For<IMembershipRepository>();
    private readonly IChargeRepository _charges = Substitute.For<IChargeRepository>();
    private readonly IMemberRepository _members = Substitute.For<IMemberRepository>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly InMemoryCheckpoint _checkpoint = new();
    private readonly FakeClock _clock = new(NoonArgentina);
    private readonly CapturingLogger<DailyMaintenanceRunner> _log = new();
    private readonly CapturingLogger<CompanyCalendar> _calendarLog = new();
    private readonly List<(Guid CompanyId, DateOnly Day)> _processed = [];

    public DailyMaintenanceRunnerTests()
    {
        _tenant.BeginScope(Arg.Any<Guid>(), Arg.Any<Guid>()).Returns(Substitute.For<IDisposable>());
        _memberships.GetActiveExpiredAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _processed.Add((call.ArgAt<Guid>(0), call.ArgAt<DateOnly>(1)));
                return new List<Membership>();
            });
        _charges.GetOverdueAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new List<Charge>());
        _members.GetByStatusAsync(Arg.Any<Guid>(), MemberStatus.Overdue, Arg.Any<CancellationToken>())
            .Returns(new List<Member>());
    }

    private DailyMaintenanceRunner Sut()
    {
        var dunning = new DunningService(
            _charges, _members, Substitute.For<INotificationSender>(),
            _tenant, new DunningConfig { Enabled = false });
        var service = new DailyMaintenanceService(_memberships, _charges, _members, dunning, _tenant);
        var calendar = new CompanyCalendar(_clock, _sites, _calendarLog);
        return new DailyMaintenanceRunner(_sites, service, _checkpoint, calendar, _log);
    }

    private static Company CompanyIn(string timezone, string taxId = "30-12345678-9") =>
        Company.Create("Gym Test", taxId, timezone);

    private void HaveCompanies(params Company[] companies) =>
        _sites.GetCompaniesAsync(Arg.Any<CancellationToken>()).Returns(companies.ToList());

    private IEnumerable<DateOnly> DaysOf(Company company) =>
        _processed.Where(p => p.CompanyId == company.Id).Select(p => p.Day);

    // ── Ventana de proceso ────────────────────────────────────────────────────

    [Fact]
    public async Task FirstRun_ProcessesOnlyToday()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);

        await Sut().RunPendingAsync();

        DaysOf(company).Should().Equal(Today);
        _checkpoint.LastRun.Should().Be(Today);
    }

    [Fact]
    public async Task CatchUp_ProcessesEveryMissedDayInOrder()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _checkpoint.LastRun = Today.AddDays(-3);

        await Sut().RunPendingAsync();

        DaysOf(company).Should().Equal(Today.AddDays(-2), Today.AddDays(-1), Today);
        _checkpoint.LastRun.Should().Be(Today);
    }

    [Fact]
    public async Task SameDayTwice_IsANoOp()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        var sut = Sut();

        await sut.RunPendingAsync();
        _processed.Clear();
        await sut.RunPendingAsync();

        _processed.Should().BeEmpty();
        _checkpoint.LastRun.Should().Be(Today);
    }

    [Fact]
    public async Task CatchUpWindow_IsCappedAt35Days()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _checkpoint.LastRun = Today.AddDays(-400);

        await Sut().RunPendingAsync();

        var days = DaysOf(company).ToList();
        days.Should().HaveCount(35);
        days.First().Should().Be(Today.AddDays(-34));
        days.Last().Should().Be(Today);
    }

    [Fact]
    public async Task CheckpointFromTheFuture_IsANoOp()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _checkpoint.LastRun = Today.AddDays(1);   // el reloj de la PC se atrasó

        await Sut().RunPendingAsync();

        _processed.Should().BeEmpty();
        _checkpoint.LastRun.Should().Be(Today.AddDays(1), "el checkpoint no retrocede");
    }

    [Fact]
    public async Task NoCompanies_DoesNothing()
    {
        HaveCompanies();

        await Sut().RunPendingAsync();

        _processed.Should().BeEmpty();
        _checkpoint.Writes.Should().Be(0);
    }

    // ── Huso horario ──────────────────────────────────────────────────────────

    [Fact]
    public async Task TheDayIsTheCompanyLocalDay_NotTheUtcDay()
    {
        // 01:30 UTC del 22/07 = 22:30 del 21/07 en Argentina (UTC-3, sin DST).
        _clock.Now = new DateTime(2026, 7, 22, 1, 30, 0, DateTimeKind.Utc);
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);

        await Sut().RunPendingAsync();

        DaysOf(company).Should().Equal(new DateOnly(2026, 7, 21));
        _checkpoint.LastRun.Should().Be(new DateOnly(2026, 7, 21));
    }

    [Fact]
    public async Task UnknownTimeZone_FallsBackToTheClockDate_AndWarns()
    {
        _clock.Now = new DateTime(2026, 7, 22, 1, 30, 0, DateTimeKind.Utc);
        var company = CompanyIn("Marte/Olympus Mons");
        HaveCompanies(company);

        await Sut().RunPendingAsync();

        DaysOf(company).Should().Equal(new DateOnly(2026, 7, 22));
        _calendarLog.Levels.Should().Contain(LogLevel.Warning);
    }

    // ── Tolerancia a fallas ───────────────────────────────────────────────────

    [Fact]
    public async Task OneCompanyFailing_IsLogged_AndTheOthersStillRun()
    {
        var broken = CompanyIn(ArgentinaTz, "30-11111111-1");
        var healthy = CompanyIn(ArgentinaTz, "30-22222222-2");
        HaveCompanies(broken, healthy);
        _memberships.GetActiveExpiredAsync(broken.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("base bloqueada"));

        var act = () => Sut().RunPendingAsync();

        await act.Should().NotThrowAsync();
        DaysOf(healthy).Should().Equal(Today);
        _log.Levels.Should().Contain(LogLevel.Error);
        _checkpoint.LastRun.Should().Be(Today, "la empresa sana completó su día");
    }

    [Fact]
    public async Task TheOnlyCompanyFailing_DoesNotAdvanceTheCheckpoint_SoItIsRetried()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _checkpoint.LastRun = Today.AddDays(-1);
        _memberships.GetActiveExpiredAsync(company.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("base bloqueada"));

        await Sut().RunPendingAsync();

        _checkpoint.LastRun.Should().Be(Today.AddDays(-1));
    }

    [Fact]
    public async Task ACompanyThatFailsOnADay_StopsItsWindow()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _checkpoint.LastRun = Today.AddDays(-3);
        _memberships.GetActiveExpiredAsync(company.Id, Today.AddDays(-1), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("fallo en el día 2"));

        await Sut().RunPendingAsync();

        DaysOf(company).Should().Equal(Today.AddDays(-2));
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowed()
    {
        var company = CompanyIn(ArgentinaTz);
        HaveCompanies(company);
        _memberships.GetActiveExpiredAsync(Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var act = () => Sut().RunPendingAsync();

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ── Varias empresas ───────────────────────────────────────────────────────

    [Fact]
    public async Task EachCompanyUsesItsOwnLocalDay()
    {
        // 02:00 UTC del 22/07: Argentina (UTC-3) todavía es 21/07; Nueva Zelanda (UTC+12) ya es 22/07 14:00.
        _clock.Now = new DateTime(2026, 7, 22, 2, 0, 0, DateTimeKind.Utc);
        var argentina = CompanyIn(ArgentinaTz, "30-11111111-1");
        var newZealand = CompanyIn("New Zealand Standard Time", "30-22222222-2");
        HaveCompanies(argentina, newZealand);

        await Sut().RunPendingAsync();

        DaysOf(argentina).Should().Equal(new DateOnly(2026, 7, 21));
        DaysOf(newZealand).Should().Equal(new DateOnly(2026, 7, 22));
        _checkpoint.LastRun.Should().Be(new DateOnly(2026, 7, 22), "avanza hasta el día local más nuevo");
    }

    // ── Dobles de prueba ──────────────────────────────────────────────────────

    private sealed class InMemoryCheckpoint : IMaintenanceCheckpoint
    {
        public DateOnly? LastRun { get; set; }
        public int Writes { get; private set; }

        public DateOnly? ReadLastRun() => LastRun;

        public void WriteLastRun(DateOnly localDay)
        {
            LastRun = localDay;
            Writes++;
        }
    }

}
