using FluentAssertions;
using GymForge.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

public sealed class LocalSettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gymforge_settings_" + Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private LocalSettingsService NewSettings(string? path = null) =>
        new(NullLogger<LocalSettingsService>.Instance, path ?? SettingsPath);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void NoFile_StartsWithDefaults()
    {
        var settings = NewSettings();

        settings.OffsiteBackupDir.Should().BeNull();
        settings.LastOffsiteBackupUtc.Should().BeNull();
        settings.UpdateFeedUrl.Should().BeNull();
    }

    [Fact]
    public void SavedValues_AreReadBackByAnotherInstance()
    {
        var lastBackup = new DateTime(2026, 10, 28, 17, 3, 0, DateTimeKind.Utc);
        var settings = NewSettings();
        settings.OffsiteBackupDir = @"F:\Respaldos";
        settings.LastOffsiteBackupUtc = lastBackup;

        settings.Save();

        var reloaded = NewSettings();
        reloaded.OffsiteBackupDir.Should().Be(@"F:\Respaldos");
        reloaded.LastOffsiteBackupUtc.Should().Be(lastBackup);
        reloaded.LastOffsiteBackupUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void UpdateFeedUrl_IsReadBackByAnotherInstance()
    {
        var settings = NewSettings();
        settings.UpdateFeedUrl = "https://github.com/acme/gymforge";

        settings.Save();

        NewSettings().UpdateFeedUrl.Should().Be("https://github.com/acme/gymforge");
    }

    [Fact]
    public void FileWrittenBeforeTheUpdateFeedExisted_StillLoads_WithoutAFeed()
    {
        // settings.json de una versión anterior: solo conoce el destino del backup externo.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ \"OffsiteBackupDir\": \"F:\\\\Respaldos\" }");

        var settings = NewSettings();

        settings.OffsiteBackupDir.Should().Be(@"F:\Respaldos");
        settings.UpdateFeedUrl.Should().BeNull();
    }

    [Fact]
    public void ClearedValues_AreReadBackAsNull()
    {
        var settings = NewSettings();
        settings.OffsiteBackupDir = @"F:\Respaldos";
        settings.LastOffsiteBackupUtc = DateTime.UtcNow;
        settings.UpdateFeedUrl = "https://github.com/acme/gymforge";
        settings.Save();

        settings.OffsiteBackupDir = null;
        settings.LastOffsiteBackupUtc = null;
        settings.UpdateFeedUrl = null;
        settings.Save();

        var reloaded = NewSettings();
        reloaded.OffsiteBackupDir.Should().BeNull();
        reloaded.LastOffsiteBackupUtc.Should().BeNull();
        reloaded.UpdateFeedUrl.Should().BeNull();
    }

    [Fact]
    public void Save_CreatesTheMissingDirectory_AndLeavesNoTemporaryFile()
    {
        Directory.Exists(_dir).Should().BeFalse();

        NewSettings().Save();

        File.Exists(SettingsPath).Should().BeTrue();
        Directory.GetFiles(_dir).Should().ContainSingle().Which.Should().Be(SettingsPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("esto no es json")]
    [InlineData("{ \"OffsiteBackupDir\": ")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{ \"LastOffsiteBackupUtc\": \"ayer\" }")]
    public void CorruptFile_StartsWithDefaults_AndDoesNotThrow(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, content);

        var create = () => NewSettings();

        var settings = create.Should().NotThrow().Which;
        settings.OffsiteBackupDir.Should().BeNull();
        settings.LastOffsiteBackupUtc.Should().BeNull();
        settings.UpdateFeedUrl.Should().BeNull();
    }

    [Fact]
    public void UnwritableLocation_NeverThrows_AndKeepsTheValuesInMemory()
    {
        // La "carpeta" de los settings es en realidad un archivo: crear el directorio y escribir fallan.
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "bloqueado");
        File.WriteAllText(blocker, "x");
        var settings = NewSettings(Path.Combine(blocker, "settings.json"));
        settings.OffsiteBackupDir = @"F:\Respaldos";

        var save = settings.Save;

        save.Should().NotThrow();
        settings.OffsiteBackupDir.Should().Be(@"F:\Respaldos");
    }

    [Fact]
    public void DefaultPath_LivesUnderLocalAppDataGymForge()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GymForge", "settings.json");

        LocalSettingsService.DefaultPath.Should().Be(expected);
    }
}
