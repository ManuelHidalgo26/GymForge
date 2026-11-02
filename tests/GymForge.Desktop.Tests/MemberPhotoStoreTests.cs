using FluentAssertions;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.Tests;

/// <summary>Reglas de la imagen (<see cref="PhotoImage"/>) y escritura de la foto en disco (<see cref="MemberPhotoStore"/>).</summary>
public sealed class MemberPhotoStoreTests : IDisposable
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gymforge_fotos_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private MemberPhotoStore Store() => new(Path.Combine(_root, "fotos"));

    // ── PhotoImage ────────────────────────────────────────────────────────────

    [Fact]
    public void RequireExtension_RecognizesJpegAndPngByTheirContent()
    {
        PhotoImage.RequireExtension(Jpeg).Should().Be(".jpg");
        PhotoImage.RequireExtension(Png).Should().Be(".png");
    }

    [Theory]
    [InlineData(new byte[] { })]                                              // vacío
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]           // GIF
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D })]                 // PDF
    [InlineData(new byte[] { 0x4D, 0x5A, 0x90, 0x00 })]                       // un .exe renombrado a .jpg
    [InlineData(new byte[] { 0xFF, 0xD8 })]                                   // JPEG truncado antes de la firma completa
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47 })]                       // PNG truncado
    public void RequireExtension_RejectsAnythingThatIsNotAJpegOrPng(byte[] content)
    {
        var act = () => PhotoImage.RequireExtension(content);

        act.Should().Throw<InvalidDataException>().WithMessage("*JPG o PNG*");
    }

    [Fact]
    public void RequireExtension_AcceptsExactlyTheMaximumSize_AndRejectsOneByteMore()
    {
        var atTheLimit = new byte[PhotoImage.MaxBytes];
        Jpeg.CopyTo(atTheLimit, 0);
        var overTheLimit = new byte[PhotoImage.MaxBytes + 1];
        Jpeg.CopyTo(overTheLimit, 0);

        PhotoImage.RequireExtension(atTheLimit).Should().Be(".jpg");
        var act = () => PhotoImage.RequireExtension(overTheLimit);
        act.Should().Throw<InvalidDataException>().WithMessage("*10 MB*");
    }

    /// <summary>Un stream que entrega bytes sin fin y cuenta cuántos le pidieron (un archivo gigante).</summary>
    private sealed class EndlessStream : Stream
    {
        public long BytesServed { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Array.Fill(buffer, (byte)0xFF, offset, count);
            BytesServed += count;
            return count;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Fact]
    public async Task ReadCappedAsync_ReadsAWholeImageThatFits()
    {
        using var stream = new MemoryStream(Png);

        (await PhotoImage.ReadCappedAsync(stream)).Should().Equal(Png);
    }

    [Fact]
    public async Task ReadCappedAsync_ReadsExactlyTheMaximumSizeInFull()
    {
        var atTheLimit = new byte[PhotoImage.MaxBytes];
        Jpeg.CopyTo(atTheLimit, 0);
        using var stream = new MemoryStream(atTheLimit);

        var image = await PhotoImage.ReadCappedAsync(stream);

        image.Should().HaveCount(PhotoImage.MaxBytes);
        PhotoImage.RequireExtension(image).Should().Be(".jpg");
    }

    [Fact]
    public async Task ReadCappedAsync_AnEndlessFile_StopsJustAfterTheLimit_AndIsRejectedBySize()
    {
        using var stream = new EndlessStream();

        var image = await PhotoImage.ReadCappedAsync(stream);

        stream.BytesServed.Should().BeLessThan(PhotoImage.MaxBytes + 200_000, "no se lee un archivo gigante entero");
        var act = () => PhotoImage.RequireExtension(image);
        act.Should().Throw<InvalidDataException>().WithMessage("*10 MB*");
    }

    // ── MemberPhotoStore ──────────────────────────────────────────────────────

    [Fact]
    public void Save_WritesTheFileUnderTheFotosFolder_NamedAfterTheMember_AndReturnsItsAbsolutePath()
    {
        var memberId = Guid.NewGuid();

        var path = Store().Save(memberId, Jpeg);

        Path.IsPathRooted(path).Should().BeTrue();
        path.Should().Be(Path.Combine(_root, "fotos", $"{memberId}.jpg"));
        File.ReadAllBytes(path).Should().Equal(Jpeg);
    }

    [Fact]
    public void Save_CreatesTheFolderTheFirstTime()
    {
        Directory.Exists(Path.Combine(_root, "fotos")).Should().BeFalse();

        Store().Save(Guid.NewGuid(), Png);

        Directory.Exists(Path.Combine(_root, "fotos")).Should().BeTrue();
    }

    [Fact]
    public void Save_ANewPhotoOfTheSameFormat_ReplacesTheFile()
    {
        var memberId = Guid.NewGuid();
        var first = Store().Save(memberId, Jpeg);
        byte[] second = [0xFF, 0xD8, 0xFF, 0xAA, 0xBB];

        var path = Store().Save(memberId, second);

        path.Should().Be(first);
        File.ReadAllBytes(path).Should().Equal(second);
        Directory.GetFiles(Path.Combine(_root, "fotos")).Should().ContainSingle();
    }

    [Fact]
    public void Save_ANewPhotoInAnotherFormat_LeavesNoOrphanOfThePreviousOne()
    {
        var memberId = Guid.NewGuid();
        var png = Store().Save(memberId, Png);

        var jpg = Store().Save(memberId, Jpeg);

        jpg.Should().EndWith(".jpg");
        File.Exists(png).Should().BeFalse();
        Directory.GetFiles(Path.Combine(_root, "fotos")).Should().ContainSingle().Which.Should().Be(jpg);
    }

    [Fact]
    public void Save_NeverTouchesThePhotoOfAnotherMember()
    {
        var other = Store().Save(Guid.NewGuid(), Png);

        Store().Save(Guid.NewGuid(), Jpeg);

        File.ReadAllBytes(other).Should().Equal(Png);
        Directory.GetFiles(Path.Combine(_root, "fotos")).Should().HaveCount(2);
    }

    [Fact]
    public void Save_AnInvalidImage_ThrowsAndWritesNothing()
    {
        var act = () => Store().Save(Guid.NewGuid(), [0x47, 0x49, 0x46]);

        act.Should().Throw<InvalidDataException>();
        Directory.Exists(Path.Combine(_root, "fotos")).Should().BeFalse("ni siquiera se crea la carpeta");
    }

    [Fact]
    public void DefaultDirectory_IsTheFotosFolderNextToTheDatabase_WhereTheDataTransferLooksForIt()
    {
        MemberPhotoStore.DefaultDirectory().Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GymForge", "fotos"));
    }
}
