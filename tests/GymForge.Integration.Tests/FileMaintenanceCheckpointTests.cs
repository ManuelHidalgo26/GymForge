using FluentAssertions;
using GymForge.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

public sealed class FileMaintenanceCheckpointTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymforge_checkpoint_" + Guid.NewGuid().ToString("N"));

    private string CheckpointPath => Path.Combine(_dir, "maintenance-last-run.txt");

    private FileMaintenanceCheckpoint NewCheckpoint(string? path = null) =>
        new(NullLogger<FileMaintenanceCheckpoint>.Instance, path ?? CheckpointPath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFile_ReadsNull() =>
        NewCheckpoint().ReadLastRun().Should().BeNull();

    [Fact]
    public void WrittenDay_IsReadBackByAnotherInstance()
    {
        NewCheckpoint().WriteLastRun(new DateOnly(2026, 7, 21));

        NewCheckpoint().ReadLastRun().Should().Be(new DateOnly(2026, 7, 21));
    }

    [Fact]
    public void StoresTheDayAsInvariantIsoText()
    {
        NewCheckpoint().WriteLastRun(new DateOnly(2026, 7, 5));

        File.ReadAllText(CheckpointPath).Trim().Should().Be("2026-07-05");
    }

    [Fact]
    public void Write_CreatesTheMissingDirectory()
    {
        Directory.Exists(_dir).Should().BeFalse();

        NewCheckpoint().WriteLastRun(new DateOnly(2026, 7, 21));

        File.Exists(CheckpointPath).Should().BeTrue();
    }

    [Fact]
    public void LaterWrite_ReplacesTheEarlierOne()
    {
        var checkpoint = NewCheckpoint();

        checkpoint.WriteLastRun(new DateOnly(2026, 7, 20));
        checkpoint.WriteLastRun(new DateOnly(2026, 7, 21));

        NewCheckpoint().ReadLastRun().Should().Be(new DateOnly(2026, 7, 21));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es una fecha")]
    [InlineData("21/07/2026")]
    public void CorruptFile_ReadsNull(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CheckpointPath, content);

        NewCheckpoint().ReadLastRun().Should().BeNull();
    }

    [Fact]
    public void UnwritableLocation_NeverThrows_AndKeepsTheDayInMemory()
    {
        // La "carpeta" del checkpoint es en realidad un archivo: crear el directorio y escribir fallan.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "bloqueado");
        File.WriteAllText(blocker, "x");
        var checkpoint = NewCheckpoint(Path.Combine(blocker, "maintenance-last-run.txt"));

        var write = () => checkpoint.WriteLastRun(new DateOnly(2026, 7, 21));

        write.Should().NotThrow();
        checkpoint.ReadLastRun().Should().Be(new DateOnly(2026, 7, 21),
            "si el disco falla, al menos esta sesión no repite el día en cada corrida");
    }

    [Fact]
    public void DefaultPath_LivesUnderLocalAppDataGymForge()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GymForge", "maintenance-last-run.txt");

        FileMaintenanceCheckpoint.DefaultPath.Should().Be(expected);
    }
}
