using FluentAssertions;
using GymForge.Application.DTOs;
using GymForge.Application.Interfaces;
using GymForge.Application.UseCases.Members;
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
/// DNI duplicado en alta y edición: el mensaje de la excepción se muestra tal cual, sin el prefijo genérico.
/// </summary>
public class DuplicateDocumentViewModelTests
{
    private const string FriendlyMessage = "Ya existe un socio con ese DNI en esta sede.";

    private readonly IMediator _mediator = Substitute.For<IMediator>();

    private static DuplicateMemberDocumentException Duplicate() => new(new InvalidOperationException("índice único"));

    private static async Task<SessionContext> SessionAsync()
    {
        var company = Company.Create("Gym Test", "30-12345678-9");
        return await TestSession.InitializedAsync(company, [Site.Create(company.Id, "Central", "Calle 1")]);
    }

    [Fact]
    public async Task Create_WhenTheDocumentIsDuplicated_ShowsTheCleanMessage_AndDoesNotCreate()
    {
        _mediator.Send(Arg.Any<CreateMemberCommand>(), Arg.Any<CancellationToken>()).Throws(Duplicate());
        var vm = new CreateMemberViewModel(
            _mediator, await SessionAsync(), new ReceiptService(Substitute.For<IServiceScopeFactory>()),
            Substitute.For<IImagePicker>(), Substitute.For<IMemberPhotoStore>())
        {
            FirstName = "Ana",
            LastName = "García",
            DocumentNumber = "30123456",
        };
        var created = new List<MemberDto>();
        vm.MemberCreated += created.Add;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be(FriendlyMessage);
        vm.IsSaving.Should().BeFalse();
        created.Should().BeEmpty();
    }

    [Fact]
    public async Task Edit_WhenTheDocumentIsDuplicated_ShowsTheCleanMessage_AndStaysEditing()
    {
        _mediator.Send(Arg.Any<UpdateMemberCommand>(), Arg.Any<CancellationToken>()).Throws(Duplicate());
        var vm = new MemberEditViewModel(_mediator, await SessionAsync());
        vm.Begin(MemberDto.FromEntity(
            Member.Create(Guid.NewGuid(), Guid.NewGuid(), "Ana", "García", DocumentType.DNI, "30123456", Gender.Female)));
        var saved = new List<MemberDto>();
        vm.Saved += saved.Add;

        await vm.SaveCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Be(FriendlyMessage);
        vm.IsSaving.Should().BeFalse();
        vm.IsEditing.Should().BeTrue();
        saved.Should().BeEmpty();
    }
}
