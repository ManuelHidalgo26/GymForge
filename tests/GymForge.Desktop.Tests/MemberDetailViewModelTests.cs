using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Access;
using GymForge.Application.UseCases.Dunning;
using GymForge.Application.UseCases.Exercises;
using GymForge.Application.UseCases.Licensing;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Memberships;
using GymForge.Application.UseCases.Routines;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Charges;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Desktop.ViewModels.Routines;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>Pestaña Membresías de la ficha: abrir el modal de freeze y reactivar una membresía congelada.</summary>
public class MemberDetailViewModelTests
{
    private static readonly DateOnly Today = new(2026, 3, 1);

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IMembershipRepository _membershipRepo = Substitute.For<IMembershipRepository>();
    private readonly MemberDto _member = MemberDto.FromEntity(
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female));

    public MemberDetailViewModelTests() =>
        _membershipRepo.GetByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<Membership>());

    private MemberDetailViewModel Sut()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var chargeRepo = Substitute.For<IChargeRepository>();
        var session = new SessionContext(
            scopeFactory, new GatekeeperConfig(), new DunningConfig(),
            new CurrentLicense(), Substitute.For<ILicenseService>());
        var charges = new ChargesViewModel(chargeRepo, _mediator, session, new ReceiptService(scopeFactory));

        return new MemberDetailViewModel(
            _mediator, _membershipRepo, Substitute.For<IAccessLogRepository>(), chargeRepo, charges,
            new RoutineBuilderViewModel(_mediator, Substitute.For<IMemberRepository>(), session),
            session, Substitute.For<IImagePicker>(), Substitute.For<IMemberPhotoStore>());
    }

    private async Task<MemberDetailViewModel> SutOnMembershipsTabAsync()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        await vm.SwitchTabCommand.ExecuteAsync(MemberDetailTab.Memberships);
        _membershipRepo.ClearReceivedCalls();
        return vm;
    }

    private static MembershipDto MembershipDtoIn(MembershipStatus status) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Mensual",
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), status, AutoRenew: true,
        VisitsRemaining: null, FreezeStart: null, FreezeEnd: null, CancelDate: null, CancelReason: null);

    private void HaveQuota(Guid membershipId, FreezeQuotaDto quota) =>
        _mediator.Send(Arg.Is<GetFreezeQuotaQuery>(q => q.MembershipId == membershipId), Arg.Any<CancellationToken>())
            .Returns(quota);

    // ── Abrir el modal de freeze ──────────────────────────────────────────────

    [Fact]
    public async Task Freeze_OpensTheModalWithTheQuotaOfTheMembership()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        HaveQuota(ms.Id, new FreezeQuotaDto(Today, 2026, MaxDays: 90, UsedDays: 20));

        await vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        vm.IsFreezeModalOpen.Should().BeTrue();
        vm.FreezeModal!.Quota.RemainingDays.Should().Be(70);
        vm.FreezeModal.StartDate.Should().Be(Today, "el día de hoy sale de la empresa, no de DateTime.Today");
        vm.MembershipsMessage.Should().BeNull();
        await _mediator.DidNotReceive().Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_WhenTheQuotaCannotBeRead_ExplainsItAndDoesNotOpenTheModal()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        _mediator.Send(Arg.Any<GetFreezeQuotaQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeyNotFoundException("Membresía no encontrada."));

        var act = () => vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        await act.Should().NotThrowAsync();
        vm.IsFreezeModalOpen.Should().BeFalse();
        vm.MembershipsMessage.Should().Be("Membresía no encontrada.");
    }

    [Fact]
    public async Task ConfirmingTheModal_ClosesIt_AndReloadsTheMemberships()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        HaveQuota(ms.Id, new FreezeQuotaDto(Today, 2026, 90, 0));
        await vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        await vm.FreezeModal!.ConfirmCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<FreezeMembershipCommand>(c => c.MembershipId == ms.Id && c.FreezeStart == Today),
            Arg.Any<CancellationToken>());
        vm.IsFreezeModalOpen.Should().BeFalse();
        await _membershipRepo.Received(1).GetByMemberAsync(_member.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConfirmingTheModal_WhenTheCapIsExceeded_KeepsItOpenWithTheMessage()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        HaveQuota(ms.Id, new FreezeQuotaDto(Today, 2026, 90, 0));
        _mediator.Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Supera el tope de 90 días de congelamiento por año."));
        await vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        await vm.FreezeModal!.ConfirmCommand.ExecuteAsync(null);

        vm.IsFreezeModalOpen.Should().BeTrue();
        vm.FreezeModal.ErrorMessage.Should().Contain("Supera el tope");
        await _membershipRepo.DidNotReceive().GetByMemberAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellingTheModal_ClosesItWithoutFreezing()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        HaveQuota(ms.Id, new FreezeQuotaDto(Today, 2026, 90, 0));
        await vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        vm.FreezeModal!.CancelCommand.Execute(null);

        vm.IsFreezeModalOpen.Should().BeFalse();
        await _mediator.DidNotReceive().Send(Arg.Any<FreezeMembershipCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Freeze_ClearsTheMessageOfAPreviousAttempt()
    {
        var vm = await SutOnMembershipsTabAsync();
        var ms = MembershipDtoIn(MembershipStatus.Active);
        _mediator.Send(Arg.Any<UnfreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("La membresía no está congelada."));
        await vm.ReactivateMembershipCommand.ExecuteAsync(ms);
        vm.MembershipsMessage.Should().NotBeNull();
        HaveQuota(ms.Id, new FreezeQuotaDto(Today, 2026, 90, 0));

        await vm.OpenFreezeModalCommand.ExecuteAsync(ms);

        vm.MembershipsMessage.Should().BeNull();
    }

    // ── Reactivar ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Reactivate_UnfreezesTheMembership_AndReloadsTheList()
    {
        var vm = await SutOnMembershipsTabAsync();
        var frozen = MembershipDtoIn(MembershipStatus.Frozen);

        await vm.ReactivateMembershipCommand.ExecuteAsync(frozen);

        await _mediator.Received(1).Send(
            Arg.Is<UnfreezeMembershipCommand>(c => c.MembershipId == frozen.Id), Arg.Any<CancellationToken>());
        await _membershipRepo.Received(1).GetByMemberAsync(_member.Id, Arg.Any<CancellationToken>());
        vm.MembershipsMessage.Should().BeNull();
    }

    [Fact]
    public async Task Reactivate_WhenItFails_ShowsTheMessage_DoesNotThrow_AndStillReloads()
    {
        var vm = await SutOnMembershipsTabAsync();
        var frozen = MembershipDtoIn(MembershipStatus.Frozen);
        _mediator.Send(Arg.Any<UnfreezeMembershipCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("La membresía no está congelada."));

        var act = () => vm.ReactivateMembershipCommand.ExecuteAsync(frozen);

        await act.Should().NotThrowAsync();
        vm.MembershipsMessage.Should().Be("La membresía no está congelada.");
        await _membershipRepo.Received(1).GetByMemberAsync(_member.Id, Arg.Any<CancellationToken>());
    }

    // ── Datos: modo edición ───────────────────────────────────────────────────

    [Fact]
    public async Task Edit_Start_EntersEditMode_WithTheCurrentValues()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);

        vm.StartEditCommand.Execute(null);

        vm.Edit.IsEditing.Should().BeTrue();
        vm.Edit.FirstName.Should().Be("Ana");
        vm.Edit.LastName.Should().Be("García");
        vm.Edit.DocumentNumber.Should().Be("30111222");
        vm.Edit.DocumentType.Should().Be(DocumentType.DNI);
    }

    [Fact]
    public async Task Edit_Save_SendsTheCommand_ReloadsTheMember_AndLeavesEditMode()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        var updated = _member with { FirstName = "Anita", FullName = "Anita García", Mobile = "+5491100001111" };
        _mediator.Send(Arg.Any<UpdateMemberCommand>(), Arg.Any<CancellationToken>()).Returns(updated);
        vm.StartEditCommand.Execute(null);
        vm.Edit.FirstName = " Anita ";
        vm.Edit.Mobile = "+5491100001111";
        vm.Edit.MedicalConditions = "Asma";

        await vm.Edit.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<UpdateMemberCommand>(c =>
                c.MemberId == _member.Id && c.FirstName == "Anita" && c.LastName == "García"
                && c.Mobile == "+5491100001111" && c.MedicalConditions == "Asma"),
            Arg.Any<CancellationToken>());
        vm.Member.Should().Be(updated);
        vm.Edit.IsEditing.Should().BeFalse();
        vm.Edit.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task Edit_Save_WhenValidationFails_StaysInEditMode_WithTheMessage_AndKeepsTheMember()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        _mediator.Send(Arg.Any<UpdateMemberCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FluentValidation.ValidationException(
                [new FluentValidation.Results.ValidationFailure("Email", "El email no es válido.")]));
        vm.StartEditCommand.Execute(null);
        vm.Edit.Email = "malo";

        await vm.Edit.SaveCommand.ExecuteAsync(null);

        vm.Edit.IsEditing.Should().BeTrue();
        vm.Edit.ErrorMessage.Should().Be("El email no es válido.");
        vm.Member.Should().Be(_member);
    }

    [Fact]
    public async Task Edit_Cancel_DiscardsTheChanges_WithoutSending()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        vm.StartEditCommand.Execute(null);
        vm.Edit.FirstName = "Otro";

        vm.Edit.CancelCommand.Execute(null);

        vm.Edit.IsEditing.Should().BeFalse();
        vm.Member.FirstName.Should().Be("Ana");
        await _mediator.DidNotReceive().Send(Arg.Any<UpdateMemberCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_CannotSave_WithBlankRequiredFields()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        vm.StartEditCommand.Execute(null);

        vm.Edit.FirstName = "  ";

        vm.Edit.SaveCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task LoadingAnotherMember_LeavesEditMode()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        vm.StartEditCommand.Execute(null);

        await vm.LoadAsync(_member with { Id = Guid.NewGuid() });

        vm.Edit.IsEditing.Should().BeFalse();
    }

    // ── Rutinas en la ficha ───────────────────────────────────────────────────

    [Fact]
    public async Task RoutinesTab_InitializesTheEmbeddedBuilderWithTheCurrentMember()
    {
        var vm = Sut();
        await vm.LoadAsync(_member);
        var routine = new RoutineDto(Guid.NewGuid(), "Full Body", WorkoutGoal.Hypertrophy, 3, 2);
        _mediator.Send(Arg.Any<GetMemberRoutinesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<RoutineDto> { routine });
        _mediator.Send(Arg.Any<SearchExercisesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new List<ExerciseDto>());

        await vm.SwitchTabCommand.ExecuteAsync(MemberDetailTab.Routines);

        vm.RoutineBuilder.SelectedMember.Should().Be(_member);
        vm.RoutineBuilder.ShowMemberPicker.Should().BeFalse();
        vm.RoutineBuilder.Routines.Should().ContainSingle().Which.Should().Be(routine);
        await _mediator.Received(1).Send(
            Arg.Is<GetMemberRoutinesQuery>(q => q.MemberId == _member.Id), Arg.Any<CancellationToken>());
    }
}
