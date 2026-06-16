using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Routines;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using NSubstitute;

namespace GymForge.Application.Tests.Handlers;

public class RoutineCommandsTests
{
    private readonly IRoutineRepository _repo = Substitute.For<IRoutineRepository>();
    private readonly Guid _companyId = Guid.NewGuid();
    private readonly Guid _memberId = Guid.NewGuid();

    [Fact]
    public async Task CreateRoutine_PersistsAndReturnsDto()
    {
        var dto = await new CreateRoutineCommandHandler(_repo).Handle(
            new CreateRoutineCommand(_companyId, _memberId, "Full Body", WorkoutGoal.Hypertrophy, 3),
            CancellationToken.None);

        dto.Name.Should().Be("Full Body");
        dto.FrequencyPerWeek.Should().Be(3);
        await _repo.Received(1).AddRoutineAsync(
            Arg.Is<Routine>(r => r.MemberId == _memberId && r.Name == "Full Body"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddRoutineDay_NumbersSequentially()
    {
        var routine = Routine.Create(_companyId, _memberId, "Full", WorkoutGoal.Strength,
            DateOnly.FromDateTime(DateTime.Today), 3);
        routine.Days.Add(RoutineDay.Create(routine.Id, 1, "Día 1"));
        _repo.GetWithDaysAsync(routine.Id, Arg.Any<CancellationToken>()).Returns(routine);

        await new AddRoutineDayCommandHandler(_repo).Handle(
            new AddRoutineDayCommand(_companyId, routine.Id, "Tren superior"), CancellationToken.None);

        await _repo.Received(1).AddDayAsync(
            Arg.Is<RoutineDay>(d => d.DayNumber == 2 && d.Name == "Tren superior"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddRoutineItem_CreatesItemWithSets()
    {
        var routine = Routine.Create(_companyId, _memberId, "Full", WorkoutGoal.Hypertrophy,
            DateOnly.FromDateTime(DateTime.Today), 3);
        var day = RoutineDay.Create(routine.Id, 1, "Día 1");
        typeof(RoutineDay).GetProperty(nameof(RoutineDay.Routine))!.SetValue(day, routine);
        _repo.GetDayAsync(day.Id, Arg.Any<CancellationToken>()).Returns(day);

        await new AddRoutineItemCommandHandler(_repo).Handle(
            new AddRoutineItemCommand(_companyId, day.Id, Guid.NewGuid(), Sets: 3, RepsMin: 8, RepsMax: 12),
            CancellationToken.None);

        await _repo.Received(1).AddItemAsync(
            Arg.Is<RoutineItem>(i => i.Rank == 1 && i.Sets.Count == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddRoutineItem_WrongTenant_Throws()
    {
        var routine = Routine.Create(Guid.NewGuid(), _memberId, "Full", WorkoutGoal.Hypertrophy,
            DateOnly.FromDateTime(DateTime.Today), 3);
        var day = RoutineDay.Create(routine.Id, 1, "Día 1");
        typeof(RoutineDay).GetProperty(nameof(RoutineDay.Routine))!.SetValue(day, routine);
        _repo.GetDayAsync(day.Id, Arg.Any<CancellationToken>()).Returns(day);

        var act = () => new AddRoutineItemCommandHandler(_repo).Handle(
            new AddRoutineItemCommand(_companyId, day.Id, Guid.NewGuid(), 3, 8, 12), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetRoutineDetail_SummarizesSets()
    {
        var routine = Routine.Create(_companyId, _memberId, "Full", WorkoutGoal.Hypertrophy,
            DateOnly.FromDateTime(DateTime.Today), 3);
        var day = RoutineDay.Create(routine.Id, 1, "Día 1");
        var item = RoutineItem.Create(day.Id, Guid.NewGuid(), 1);
        item.Sets.Add(RoutineItemSet.Create(item.Id, 1, 8, 12));
        item.Sets.Add(RoutineItemSet.Create(item.Id, 2, 8, 12));
        item.Sets.Add(RoutineItemSet.Create(item.Id, 3, 8, 12));
        day.Items.Add(item);
        routine.Days.Add(day);
        _repo.GetDetailAsync(routine.Id, Arg.Any<CancellationToken>()).Returns(routine);

        var detail = await new GetRoutineDetailQueryHandler(_repo).Handle(
            new GetRoutineDetailQuery(_companyId, routine.Id), CancellationToken.None);

        detail.Should().NotBeNull();
        detail!.Days.Should().ContainSingle();
        detail.Days[0].Items.Should().ContainSingle(i => i.SetsSummary == "3×8-12");
    }
}
