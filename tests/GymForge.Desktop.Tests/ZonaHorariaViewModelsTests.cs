using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Classes;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Desktop.ViewModels.Classes;
using GymForge.Desktop.ViewModels.Dashboard;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>
/// El "hoy" de las pantallas es el día del gimnasio (<see cref="ICompanyCalendar"/>), no el de la zona de la PC.
/// El día fijo cae lejos de hoy para que un <c>DateTime.Today</c> filtrado se note.
/// </summary>
public class ZonaHorariaViewModelsTests
{
    private static readonly DateOnly Today = new(2026, 7, 22);           // miércoles
    private static readonly DateOnly Monday = new(2026, 7, 20);

    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");   // Argentina: UTC-3
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private SessionContext _session = null!;

    private async Task ArrangeAsync()
    {
        _session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(Today);
        _calendar.TodayFor(Arg.Any<Company>()).Returns(Today);
    }

    private static DateTime Utc(DateOnly day) => day.ToDateTime(new TimeOnly(3, 0), DateTimeKind.Utc);

    // ── Cobros ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cobros_Vencidos_ConsultanLaMoraAlDiaDelGimnasio()
    {
        await ArrangeAsync();
        var chargeRepo = Substitute.For<IChargeRepository>();
        chargeRepo.GetOverdueBySiteAsync(_company.Id, _session.SiteId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<Charge>());
        var sut = new ChargesViewModel(
            chargeRepo, _mediator, _session, new ReceiptService(Substitute.For<IServiceScopeFactory>()), _calendar)
        {
            StatusFilter = ChargeStatusFilter.Overdue,
        };

        await sut.LoadAsync();

        await chargeRepo.Received().GetOverdueBySiteAsync(
            _company.Id, _session.SiteId, Today, Arg.Any<CancellationToken>());
        await chargeRepo.DidNotReceive().GetOverdueAsync(
            Arg.Any<Guid>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ── Clases ───────────────────────────────────────────────────────────────

    private ClassesViewModel ClassesSut()
    {
        var members = Substitute.For<IMemberRepository>();
        members.GetPagedAsync(_company.Id, Arg.Any<Guid>(), 1, 500, Arg.Any<MemberStatus?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Member>());
        _mediator.Send(Arg.Any<GetClassesQuery>(), Arg.Any<CancellationToken>()).Returns(new List<ClassDto>());
        _mediator.Send(Arg.Any<GetWeekSchedulesQuery>(), Arg.Any<CancellationToken>()).Returns(new List<ScheduleRowDto>());
        return new ClassesViewModel(_mediator, members, _session, _calendar);
    }

    [Fact]
    public async Task Clases_PrimeraCarga_PideLaSemanaDelDiaDelGimnasio()
    {
        await ArrangeAsync();
        var sut = ClassesSut();

        await sut.LoadAsync();

        sut.WeekStart.Should().Be(Monday);
        sut.ScheduleDate.Should().Be(Monday);
        await _mediator.Received().Send(
            Arg.Is<GetWeekSchedulesQuery>(q => q.WeekStart == Monday), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Clases_EstaSemana_VuelveALaSemanaDelDiaDelGimnasio()
    {
        await ArrangeAsync();
        var sut = ClassesSut();
        await sut.LoadAsync();
        sut.NextWeekCommand.Execute(null);

        await sut.ThisWeekCommand.ExecuteAsync(null);

        sut.WeekStart.Should().Be(Monday);
    }

    // ── Dashboard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dashboard_PideLosAccesosDeHoyConLosLimitesUtcDelDiaDelGimnasio()
    {
        await ArrangeAsync();
        var sites = Substitute.For<ISiteRepository>();
        sites.GetCompanyAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(_company);
        var logs = Substitute.For<IAccessLogRepository>();
        logs.GetBySiteAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<AccessLog>());
        var charges = Substitute.For<IChargeRepository>();
        charges.GetOverdueBySiteAsync(_company.Id, _session.SiteId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<Charge>());
        var payments = Substitute.For<IPaymentRepository>();
        payments.GetByPeriodAsync(_company.Id, Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new List<Payment>());
        var memberships = Substitute.For<IMembershipRepository>();
        memberships.GetExpiringAsync(_company.Id, Today, Today.AddDays(7), Arg.Any<CancellationToken>())
            .Returns(new List<Membership>());
        var scoped = new ServiceCollection();
        scoped.AddScoped(_ => Substitute.For<IMemberRepository>());
        scoped.AddScoped(_ => payments);
        scoped.AddScoped(_ => logs);
        scoped.AddScoped(_ => charges);
        scoped.AddScoped(_ => memberships);
        scoped.AddScoped(_ => sites);
        scoped.AddScoped(_ => _calendar);
        using var provider = scoped.BuildServiceProvider();
        var sut = new DashboardViewModel(
            provider.GetRequiredService<IServiceScopeFactory>(), _session);

        await sut.LoadCommand.ExecuteAsync(null);

        await logs.Received(1).GetBySiteAsync(
            _session.SiteId, Utc(Today), Utc(Today.AddDays(1)), Arg.Any<CancellationToken>());
        await charges.Received(1).GetOverdueBySiteAsync(
            _company.Id, _session.SiteId, Today, Arg.Any<CancellationToken>());
        await memberships.Received(1).GetExpiringAsync(_company.Id, Today, Today.AddDays(7), Arg.Any<CancellationToken>());
        sut.TodayLabel.Should().Contain("22");
    }
}
