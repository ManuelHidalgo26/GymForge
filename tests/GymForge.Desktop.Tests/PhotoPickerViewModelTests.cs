using FluentAssertions;
using GymForge.Desktop.Services;
using GymForge.Desktop.ViewModels.Members;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>El cuadro de la foto: elegir archivo / pegar, validar y avisar. El picker real (diálogo, portapapeles) se verifica a mano.</summary>
public class PhotoPickerViewModelTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly IImagePicker _picker = Substitute.For<IImagePicker>();

    private PhotoPickerViewModel Sut() => new(_picker);

    [Fact]
    public async Task Pick_AValidFile_BecomesThePendingImage_AndRaisesImageChosen()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        var vm = Sut();
        byte[]? chosen = null;
        vm.ImageChosen += image => chosen = image;

        await vm.PickCommand.ExecuteAsync(null);

        vm.PendingImage.Should().Equal(Jpeg);
        vm.HasPendingImage.Should().BeTrue();
        chosen.Should().Equal(Jpeg);
        vm.Message.Should().BeNull();
    }

    [Fact]
    public async Task Pick_Cancelled_ChangesNothingAndSaysNothing()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var vm = Sut();
        var raised = false;
        vm.ImageChosen += _ => raised = true;

        await vm.PickCommand.ExecuteAsync(null);

        vm.PendingImage.Should().BeNull();
        vm.Message.Should().BeNull();
        raised.Should().BeFalse();
    }

    [Fact]
    public async Task Pick_ANonImageFile_IsRejectedWithTheReason()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var vm = Sut();
        var raised = false;
        vm.ImageChosen += _ => raised = true;

        await vm.PickCommand.ExecuteAsync(null);

        vm.PendingImage.Should().BeNull();
        vm.Message.Should().Contain("JPG o PNG");
        raised.Should().BeFalse();
    }

    [Fact]
    public async Task Pick_WhenReadingTheFileFails_ReportsItWithoutThrowing()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Throws(new IOException("el archivo está en uso"));
        var vm = Sut();

        var act = () => vm.PickCommand.ExecuteAsync(null);

        await act.Should().NotThrowAsync();
        vm.Message.Should().Contain("el archivo está en uso");
        vm.PendingImage.Should().BeNull();
    }

    [Fact]
    public async Task Paste_AnImageFromTheClipboard_BecomesThePendingImage()
    {
        _picker.PasteAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        var vm = Sut();

        await vm.PasteCommand.ExecuteAsync(null);

        vm.PendingImage.Should().Equal(Jpeg);
        vm.Message.Should().BeNull();
    }

    [Fact]
    public async Task Paste_WithNothingOnTheClipboard_SaysSo()
    {
        _picker.PasteAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        var vm = Sut();

        await vm.PasteCommand.ExecuteAsync(null);

        vm.PendingImage.Should().BeNull();
        vm.Message.Should().Contain("portapapeles");
    }

    [Fact]
    public async Task APreviousMessage_IsClearedByTheNextAttempt()
    {
        var vm = Sut();
        _picker.PasteAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);
        await vm.PasteCommand.ExecuteAsync(null);
        vm.Message.Should().NotBeNull();
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);

        await vm.PickCommand.ExecuteAsync(null);

        vm.Message.Should().BeNull();
    }

    [Fact]
    public async Task Clear_DropsThePendingImage()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        var vm = Sut();
        await vm.PickCommand.ExecuteAsync(null);

        vm.ClearCommand.Execute(null);

        vm.PendingImage.Should().BeNull();
        vm.HasPendingImage.Should().BeFalse();
        vm.Preview.Should().BeNull();
    }

    [Fact]
    public async Task MarkSaved_StopsTheImageFromBeingPending()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        var vm = Sut();
        await vm.PickCommand.ExecuteAsync(null);

        vm.MarkSaved();

        vm.PendingImage.Should().BeNull();
        vm.HasPendingImage.Should().BeFalse();
    }

    [Fact]
    public async Task HasPendingImage_NotifiesWhenItChanges()
    {
        _picker.PickFileAsync(Arg.Any<CancellationToken>()).Returns(Jpeg);
        var vm = Sut();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await vm.PickCommand.ExecuteAsync(null);
        vm.MarkSaved();

        changes.Count(name => name == nameof(PhotoPickerViewModel.HasPendingImage)).Should().Be(2);
    }

    [Fact]
    public void ShowSaved_WithAMissingFile_LeavesNoPreview_AndDoesNotThrow()
    {
        var vm = Sut();

        var act = () => vm.ShowSaved(@"C:\no\existe\foto.jpg");

        act.Should().NotThrow();
        vm.Preview.Should().BeNull();
    }

    [Fact]
    public void ShowSaved_WithoutAPath_LeavesNoPreview()
    {
        var vm = Sut();

        vm.ShowSaved(null);

        vm.Preview.Should().BeNull();
    }
}
