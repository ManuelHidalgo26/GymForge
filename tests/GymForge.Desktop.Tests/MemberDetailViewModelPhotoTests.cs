using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
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

/// <summary>Pestaña Datos de la ficha: la foto se guarda apenas se elige, y la credencial se asigna sin abrir un editor completo.</summary>
public class MemberDetailViewModelPhotoTests
{
    private const string PhotoPath = @"C:\datos\fotos\ana.jpg";
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IImagePicker _picker = Substitute.For<IImagePicker>();
    private readonly IMemberPhotoStore _photoStore = Substitute.For<IMemberPhotoStore>();
    private readonly MemberDto _member = MemberDto.FromEntity(
        Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30111222", Gender.Female));

    private async Task<(MemberDetailViewModel Vm, Company Company)> SutAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        var session = await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var chargeRepo = Substitute.For<IChargeRepository>();
        var charges = new ChargesViewModel(chargeRepo, _mediator, session, new ReceiptService(scopeFactory));

        var vm = new MemberDetailViewModel(
            _mediator, Substitute.For<IMembershipRepository>(), Substitute.For<IAccessLogRepository>(),
            chargeRepo, charges, new RoutineBuilderViewModel(_mediator, Substitute.For<IMemberRepository>(), session),
            session, _picker, _photoStore);
        await vm.LoadAsync(_member);
        return (vm, company);
    }

    // ── Foto ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PickPhoto_SavesItRightAway_AndUpdatesTheMemberOnScreen()
    {
        var (vm, company) = await SutAsync();
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        _photoStore.Save(_member.Id, Arg.Any<byte[]>()).Returns(PhotoPath);
        _mediator.Send(Arg.Any<UpdateMemberPhotoCommand>(), Arg.Any<CancellationToken>())
            .Returns(_member with { PhotoUrl = PhotoPath });

        await vm.Photo.PickCommand.ExecuteAsync(null);

        _photoStore.Received(1).Save(_member.Id, Arg.Is<byte[]>(b => b.SequenceEqual(Jpeg)));
        await _mediator.Received(1).Send(
            Arg.Is<UpdateMemberPhotoCommand>(c =>
                c.CompanyId == company.Id && c.MemberId == _member.Id && c.PhotoPath == PhotoPath),
            Arg.Any<CancellationToken>());
        vm.Member.PhotoUrl.Should().Be(PhotoPath);
        vm.Photo.HasPendingImage.Should().BeFalse("ya se guardó");
        vm.Photo.Message.Should().BeNull();
    }

    [Fact]
    public async Task PickPhoto_WhenSavingFails_ShowsTheReason_AndKeepsTheMemberAsItWas()
    {
        var (vm, _) = await SutAsync();
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        _photoStore.Save(_member.Id, Arg.Any<byte[]>()).Throws(new IOException("disco lleno"));

        await vm.Photo.PickCommand.ExecuteAsync(null);

        vm.Photo.Message.Should().Contain("No se pudo guardar la foto").And.Contain("disco lleno");
        vm.Member.PhotoUrl.Should().BeNull();
        await _mediator.DidNotReceive().Send(Arg.Any<UpdateMemberPhotoCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PickPhoto_Cancelled_SavesNothing()
    {
        var (vm, _) = await SutAsync();
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        await vm.Photo.PickCommand.ExecuteAsync(null);

        _photoStore.DidNotReceiveWithAnyArgs().Save(default, default!);
    }

    [Fact]
    public async Task PastePhoto_SavesTheClipboardImage()
    {
        var (vm, _) = await SutAsync();
        _picker.PasteAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        _photoStore.Save(_member.Id, Arg.Any<byte[]>()).Returns(PhotoPath);
        _mediator.Send(Arg.Any<UpdateMemberPhotoCommand>(), Arg.Any<CancellationToken>())
            .Returns(_member with { PhotoUrl = PhotoPath });

        await vm.Photo.PasteCommand.ExecuteAsync(null);

        vm.Member.PhotoUrl.Should().Be(PhotoPath);
    }

    // ── Credencial ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveCredential_AssignsIt_UpdatesTheMember_AndClearsTheField()
    {
        var (vm, company) = await SutAsync();
        _mediator.Send(Arg.Any<AssignCredentialCommand>(), Arg.Any<CancellationToken>())
            .Returns(_member with { TagSerial = "CARNET-77" });
        vm.NewCredential = "CARNET-77";

        await vm.SaveCredentialCommand.ExecuteAsync(null);

        await _mediator.Received(1).Send(
            Arg.Is<AssignCredentialCommand>(c =>
                c.CompanyId == company.Id && c.MemberId == _member.Id && c.TagSerial == "CARNET-77"),
            Arg.Any<CancellationToken>());
        vm.Member.TagSerial.Should().Be("CARNET-77");
        vm.NewCredential.Should().BeEmpty();
        vm.CredentialAssigned.Should().BeTrue();
        vm.CredentialMessage.Should().Be("Credencial asignada.");
    }

    [Fact]
    public async Task SaveCredential_OfAnotherMembersCredential_ShowsTheReason_AndKeepsTheInput()
    {
        var (vm, _) = await SutAsync();
        _mediator.Send(Arg.Any<AssignCredentialCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Esa credencial ya está asignada a otro socio."));
        vm.NewCredential = "CARNET-77";

        await vm.SaveCredentialCommand.ExecuteAsync(null);

        vm.CredentialMessage.Should().Be("Esa credencial ya está asignada a otro socio.");
        vm.CredentialAssigned.Should().BeFalse();
        vm.NewCredential.Should().Be("CARNET-77");
        vm.Member.TagSerial.Should().BeNull();
    }

    [Fact]
    public async Task SaveCredential_Empty_ShowsTheValidationMessage()
    {
        var (vm, _) = await SutAsync();
        _mediator.Send(Arg.Any<AssignCredentialCommand>(), Arg.Any<CancellationToken>())
            .Throws(new ValidationException([new ValidationFailure("TagSerial", "Ingresá el código de la credencial.")]));

        await vm.SaveCredentialCommand.ExecuteAsync(null);

        vm.CredentialMessage.Should().Be("Ingresá el código de la credencial.");
        vm.CredentialAssigned.Should().BeFalse();
    }

    [Fact]
    public async Task SaveCredential_ASecondTime_ClearsThePreviousMessageFirst()
    {
        var (vm, _) = await SutAsync();
        _mediator.Send(Arg.Any<AssignCredentialCommand>(), Arg.Any<CancellationToken>())
            .Returns(_member with { TagSerial = "CARNET-77" });
        vm.NewCredential = "CARNET-77";
        await vm.SaveCredentialCommand.ExecuteAsync(null);
        _mediator.Send(Arg.Any<AssignCredentialCommand>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("falló"));
        vm.NewCredential = "OTRO";

        await vm.SaveCredentialCommand.ExecuteAsync(null);

        vm.CredentialAssigned.Should().BeFalse();
        vm.CredentialMessage.Should().Be("falló");
    }
}
