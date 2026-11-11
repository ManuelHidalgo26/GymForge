using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.UseCases.Members;
using GymForge.Application.UseCases.Sales;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Members;
using GymForge.Domain.Entities;
using GymForge.Domain.Enums;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>
/// Alta de socio con foto (best-effort pero sin perderla en silencio) y credencial. La foto necesita el Id del socio:
/// se elige antes, se guarda después de crearlo.
/// </summary>
public class CreateMemberViewModelPhotoTests
{
    private const string PhotoPath = @"C:\datos\fotos\ana.jpg";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IImagePicker _picker = Substitute.For<IImagePicker>();
    private readonly IMemberPhotoStore _photoStore = Substitute.For<IMemberPhotoStore>();
    private readonly MemberDto _created = MemberDto.FromEntity(
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30123456", Gender.Female));

    public CreateMemberViewModelPhotoTests()
    {
        _mediator.Send(Arg.Any<CreateMemberCommand>(), Arg.Any<CancellationToken>()).Returns(_created);
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        _photoStore.Save(_created.Id, Arg.Any<byte[]>()).Returns(PhotoPath);
        _mediator.Send(Arg.Any<UpdateMemberPhotoCommand>(), Arg.Any<CancellationToken>()).Returns(_created);
    }

    private async Task<(CreateMemberViewModel Vm, List<MemberDto> Created, Company Company)> SutAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var session = await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
        var vm = new CreateMemberViewModel(
            _mediator, session, new ReceiptService(Substitute.For<IServiceScopeFactory>()), _picker, _photoStore)
        {
            FirstName = "Ana",
            LastName = "García",
            DocumentNumber = "30123456",
        };
        var created = new List<MemberDto>();
        vm.MemberCreated += created.Add;
        return (vm, created, company);
    }

    // ── Foto ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_WithAChosenPhoto_SavesItForTheNewMember_AndRecordsThePath()
    {
        var (vm, created, company) = await SutAsync();
        await vm.Photo.PickCommand.ExecuteAsync(null);

        await vm.SaveCommand.ExecuteAsync(null);

        _photoStore.Received(1).Save(_created.Id, Arg.Is<byte[]>(b => b.SequenceEqual(Jpeg)));
        await _mediator.Received(1).Send(
            Arg.Is<UpdateMemberPhotoCommand>(c =>
                c.CompanyId == company.Id && c.MemberId == _created.Id && c.PhotoPath == PhotoPath),
            Arg.Any<CancellationToken>());
        vm.Photo.HasPendingImage.Should().BeFalse("ya se guardó");
        _photoStore.Received(1).DeleteOtherFormats(_created.Id, PhotoPath);
        vm.ErrorMessage.Should().BeNull();
        created.Should().ContainSingle().Which.Id.Should().Be(_created.Id);
    }

    [Fact]
    public async Task Save_WithoutAPhoto_NeverTouchesTheStoreNorTheCommand()
    {
        var (vm, created, _) = await SutAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        _photoStore.DidNotReceiveWithAnyArgs().Save(default, default!);
        await _mediator.DidNotReceive().Send(Arg.Any<UpdateMemberPhotoCommand>(), Arg.Any<CancellationToken>());
        created.Should().ContainSingle();
    }

    [Fact]
    public async Task Save_WhenThePhotoFails_KeepsTheMember_ReportsIt_AndDoesNotLeaveTheForm()
    {
        var (vm, created, _) = await SutAsync();
        await vm.Photo.PickCommand.ExecuteAsync(null);
        _photoStore.Save(_created.Id, Arg.Any<byte[]>()).Throws(new IOException("disco lleno"));

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("socio se guardó").And.Contain("foto").And.Contain("disco lleno");
        created.Should().BeEmpty("no se avanza hasta resolver la foto");
        vm.Photo.HasPendingImage.Should().BeTrue("la foto sigue pendiente para reintentar");
    }

    [Fact]
    public async Task Save_RetryingAfterAPhotoFailure_DoesNotCreateTheMemberTwice_AndSavesThePhoto()
    {
        var (vm, created, _) = await SutAsync();
        await vm.Photo.PickCommand.ExecuteAsync(null);
        _photoStore.Save(_created.Id, Arg.Any<byte[]>()).Throws(new IOException("disco lleno"));
        await vm.SaveCommand.ExecuteAsync(null);
        _photoStore.Save(_created.Id, Arg.Any<byte[]>()).Returns(PhotoPath);

        await vm.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(Arg.Any<CreateMemberCommand>(), Arg.Any<CancellationToken>());
        vm.ErrorMessage.Should().BeNull();
        vm.Photo.HasPendingImage.Should().BeFalse();
        created.Should().ContainSingle();
    }

    [Fact]
    public async Task Save_RemovingThePhotoAfterAFailure_LetsTheSignUpFinish()
    {
        var (vm, created, _) = await SutAsync();
        await vm.Photo.PickCommand.ExecuteAsync(null);
        _photoStore.Save(_created.Id, Arg.Any<byte[]>()).Throws(new IOException("disco lleno"));
        await vm.SaveCommand.ExecuteAsync(null);

        vm.Photo.ClearCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);

        created.Should().ContainSingle();
        _photoStore.Received(1).Save(Arg.Any<Guid>(), Arg.Any<byte[]>());   // solo el intento fallido
    }

    [Fact]
    public async Task Save_WhenTheChargeFailsAfterThePhotoWasSaved_TheRetryDoesNotSaveThePhotoAgain()
    {
        var (vm, _, _) = await SutAsync();
        var plan = new MembershipTypeDto(Guid.NewGuid(), "Mensual", 35_000m, "1 mes", MembershipBasis.Renewal, true, VisitsIncluded: null);
        vm.Plans = [plan];
        vm.ChargeNow = true;
        vm.SelectedPlan = plan;
        await vm.Photo.PickCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<SellMembershipCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("no hay caja abierta"));

        await vm.SaveCommand.ExecuteAsync(null);
        await vm.SaveCommand.ExecuteAsync(null);

        _photoStore.Received(1).Save(_created.Id, Arg.Any<byte[]>());
        vm.ErrorMessage.Should().Contain("cobro falló");
    }

    [Fact]
    public async Task Save_WhenTheMemberCannotBeCreated_DoesNotTouchTheStore()
    {
        var (vm, created, _) = await SutAsync();
        await vm.Photo.PickCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<CreateMemberCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Esa credencial ya está asignada a otro socio."));

        await vm.SaveCommand.ExecuteAsync(null);

        _photoStore.DidNotReceiveWithAnyArgs().Save(default, default!);
        created.Should().BeEmpty();
        vm.ErrorMessage.Should().Contain("credencial");
        vm.Photo.HasPendingImage.Should().BeTrue();
    }

    // ── Credencial ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_SendsTheCredentialInTheCreateCommand()
    {
        var (vm, _, _) = await SutAsync();
        vm.TagSerial = "CARNET-77";

        await vm.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<CreateMemberCommand>(c => c.TagSerial == "CARNET-77" && c.DocumentNumber == "30123456"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Save_WithoutACredential_SendsNone()
    {
        var (vm, _, _) = await SutAsync();

        await vm.SaveCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<CreateMemberCommand>(c => c.TagSerial == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScanCredential_AsksTheViewToFocusTheCredentialField()
    {
        var (vm, _, _) = await SutAsync();
        var requested = 0;
        vm.CredentialFocusRequested += () => requested++;

        vm.ScanCredentialCommand.Execute(null);

        requested.Should().Be(1);
    }
}
