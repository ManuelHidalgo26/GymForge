using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Exercises;
using GymForge.Application.UseCases.Routines;
using GymForge.Desktop.ViewModels.Routines;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using NSubstitute;

namespace GymForge.Desktop.Tests;

/// <summary>Armador de rutinas: modo hub (con buscador de socios) y modo embebido en la ficha.</summary>
public class RoutineBuilderViewModelTests
{
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IMemberRepository _memberRepo = Substitute.For<IMemberRepository>();
    private readonly Company _company = Company.Create("Gym Test", "30-12345678-9");
    private readonly MemberDto _member;
    private readonly ExerciseDto _squat = new(Guid.NewGuid(), "Sentadilla", MuscleGroup.Quads, Equipment.Barbell, MovementType.Compound, 2);
    private readonly RoutineDto _routine = new(Guid.NewGuid(), "Full Body", WorkoutGoal.Hypertrophy, 3, 1);

    public RoutineBuilderViewModelTests()
    {
        _member = MemberDto.FromEntity(
            Member.Create(_company.Id, Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female));
        _mediator.Send(Arg.Any<SearchExercisesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExerciseDto> { _squat });
        _mediator.Send(Arg.Any<GetMemberRoutinesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RoutineDto> { _routine });
    }

    private async Task<RoutineBuilderViewModel> EmbeddedSutAsync()
    {
        var session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        var vm = new RoutineBuilderViewModel(_mediator, _memberRepo, session);
        await vm.InitForMemberAsync(_member);
        return vm;
    }

    [Fact]
    public async Task InitForMember_SetsTheMember_LoadsExercisesAndRoutines_AndHidesThePicker()
    {
        var vm = await EmbeddedSutAsync();

        vm.SelectedMember.Should().Be(_member);
        vm.ShowMemberPicker.Should().BeFalse();
        vm.HasMember.Should().BeTrue();
        vm.Exercises.Should().ContainSingle().Which.Should().Be(_squat);
        vm.Routines.Should().ContainSingle().Which.Should().Be(_routine);
        await _mediator.Received(1).Send(
            Arg.Is<GetMemberRoutinesQuery>(q => q.CompanyId == _company.Id && q.MemberId == _member.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitForMember_DoesNotLoadTheListOfMembers_NorWhenTheViewTriggersLoad()
    {
        var vm = await EmbeddedSutAsync();

        await vm.LoadCommand.ExecuteAsync(null);   // la vista lo dispara al asignarle el DataContext

        vm.Members.Should().BeEmpty();
        await _memberRepo.DidNotReceive().GetPagedAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<MemberStatus?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InitForMember_CalledAgain_ReloadsRoutinesButNotTheExercises()
    {
        var vm = await EmbeddedSutAsync();

        await vm.InitForMemberAsync(_member);

        await _mediator.Received(2).Send(Arg.Any<GetMemberRoutinesQuery>(), Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(Arg.Any<SearchExercisesQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Embedded_CreateRoutine_UsesTheSameCommand_ForTheGivenMember()
    {
        var vm = await EmbeddedSutAsync();
        _mediator.Send(Arg.Any<CreateRoutineCommand>(), Arg.Any<CancellationToken>()).Returns(_routine);
        vm.OpenRoutineFormCommand.Execute(null);
        vm.RoutineName = "Full Body";

        await vm.CreateRoutineCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<CreateRoutineCommand>(c =>
                c.CompanyId == _company.Id && c.MemberId == _member.Id && c.Name == "Full Body"),
            Arg.Any<CancellationToken>());
        vm.SelectedRoutine.Should().Be(_routine);
        vm.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Embedded_AddDayAndExercise_SendTheSameCommandsAsTheHub()
    {
        var vm = await EmbeddedSutAsync();
        var day = new RoutineDayDto(Guid.NewGuid(), 1, "Día 1", []);
        _mediator.Send(Arg.Any<GetRoutineDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(new RoutineDetailDto(_routine.Id, "Full Body", WorkoutGoal.Hypertrophy, 3, [day]));
        vm.SelectedRoutine = _routine;

        vm.NewDayName = "Piernas";
        await vm.AddDayCommand.ExecuteAsync(null);
        vm.OpenItemFormCommand.Execute(day);
        await vm.AddItemCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<AddRoutineDayCommand>(c => c.RoutineId == _routine.Id && c.Name == "Piernas"),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<AddRoutineItemCommand>(c => c.RoutineDayId == day.Id && c.ExerciseId == _squat.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hub_Load_StillLoadsMembersAndExercises_AndKeepsThePickerVisible()
    {
        var session = await TestSession.InitializedAsync(_company, [Site.Create(_company.Id, "Central", "Calle 1")]);
        _memberRepo.GetPagedAsync(session.CompanyId, session.SiteId, 1, 500, Arg.Any<MemberStatus?>(), Arg.Any<CancellationToken>())
            .Returns(new List<Member> { Member.Create(_company.Id, session.SiteId, "Ana", "García", DocumentType.DNI, "1", Gender.Female) });
        var vm = new RoutineBuilderViewModel(_mediator, _memberRepo, session);

        await vm.LoadCommand.ExecuteAsync(null);

        vm.ShowMemberPicker.Should().BeTrue();
        vm.Members.Should().ContainSingle();
        vm.Exercises.Should().ContainSingle();
    }
}
