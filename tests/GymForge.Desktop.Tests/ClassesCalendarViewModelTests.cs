using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.Services;
using GymForge.Application.UseCases.Classes;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Classes;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Proyección de la agenda semanal de Clases en 7 columnas (Lun–Dom).</summary>
public class ClassesCalendarViewModelTests
{
    private static readonly DateOnly Today = new(2026, 7, 22);           // miércoles
    private static readonly DateOnly Monday = new(2026, 7, 20);

    private readonly Company _company = Company.Create("Power Gym", "30-71234567-3");
    private readonly ICompanyCalendar _calendar = Substitute.For<ICompanyCalendar>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private SessionContext _session = null!;

    private async Task<ClassesViewModel> SutAsync(params ScheduleRowDto[] schedules)
    {
        _session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        _calendar.TodayAsync(_company.Id, Arg.Any<CancellationToken>()).Returns(Today);
        var members = Substitute.For<IMemberRepository>();
        members.GetPagedAsync(_company.Id, Arg.Any<Guid>(), 1, 500, Arg.Any<MemberStatus?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Member>());
        _mediator.Send(Arg.Any<GetClassesQuery>(), Arg.Any<CancellationToken>()).Returns(new List<ClassDto>());
        _mediator.Send(Arg.Any<GetWeekSchedulesQuery>(), Arg.Any<CancellationToken>()).Returns(schedules.ToList());
        _mediator.Send(Arg.Any<GetScheduleBookingsQuery>(), Arg.Any<CancellationToken>()).Returns(new List<BookingRowDto>());
        return new ClassesViewModel(_mediator, members, _session, _calendar);
    }

    private static ScheduleRowDto Row(DateOnly day, int hour, string name = "Funcional") =>
        new(Guid.NewGuid(), Guid.NewGuid(), name,
            day.ToDateTime(new TimeOnly(hour, 0)), day.ToDateTime(new TimeOnly(hour + 1, 0)), 20, 5);

    [Fact]
    public async Task WeekDays_AgrupaPorDiaYOrdenaPorHora()
    {
        var lunTarde = Row(Monday, 19);
        var lunManana = Row(Monday, 8);
        var mar = Row(Monday.AddDays(1), 10);
        var sut = await SutAsync(lunTarde, mar, lunManana);

        await sut.LoadAsync();

        sut.WeekDays.Should().HaveCount(7);
        sut.WeekDays.Select(d => d.Date).Should().Equal(Enumerable.Range(0, 7).Select(Monday.AddDays));
        sut.WeekDays.Select(d => d.DayName).Should().Equal("Lun", "Mar", "Mié", "Jue", "Vie", "Sáb", "Dom");
        sut.WeekDays[0].Schedules.Should().Equal(lunManana, lunTarde);
        sut.WeekDays[1].Schedules.Should().Equal(mar);
        sut.WeekDays[0].DateLabel.Should().Be("20/07");
    }

    [Fact]
    public async Task WeekDays_DiaVacioConviveConDiaConHorarios()
    {
        var sut = await SutAsync(Row(Monday, 9), Row(Monday, 18));

        await sut.LoadAsync();

        sut.WeekDays[0].IsEmpty.Should().BeFalse();
        sut.WeekDays.Skip(1).Should().OnlyContain(d => d.IsEmpty);
    }

    [Fact]
    public async Task WeekDays_SemanaSinHorarios_SieteColumnasVacias()
    {
        var sut = await SutAsync();

        await sut.LoadAsync();

        sut.WeekDays.Should().HaveCount(7).And.OnlyContain(d => d.IsEmpty);
        sut.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task SelectSchedule_SeleccionaElHorarioYCargaSusReservas()
    {
        var row = Row(Monday, 18);
        var sut = await SutAsync(row);
        await sut.LoadAsync();

        sut.SelectScheduleCommand.Execute(row);

        sut.SelectedSchedule.Should().Be(row);
        sut.HasSelection.Should().BeTrue();
        await _mediator.Received().Send(
            Arg.Is<GetScheduleBookingsQuery>(q => q.ScheduleId == row.Id && q.CompanyId == _company.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IsToday_MarcaSoloElDiaDelGimnasio_YNingunoFueraDeLaSemana()
    {
        var sut = await SutAsync();
        await sut.LoadAsync();

        sut.WeekDays.Where(d => d.IsToday).Select(d => d.Date).Should().Equal(Today);

        sut.NextWeekCommand.Execute(null);
        await Task.Yield();

        sut.WeekDays.Should().OnlyContain(d => !d.IsToday);
    }
}
