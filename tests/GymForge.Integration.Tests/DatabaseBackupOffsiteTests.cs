using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Copia del último backup a un destino externo (pendrive, red, carpeta sincronizada) para sobrevivir a que
/// se rompa el disco de la PC. Es best-effort: un destino caído nunca rompe el backup local ni la operación de negocio.
/// </summary>
public sealed class DatabaseBackupOffsiteTests : IDisposable
{
    private const string OffsiteFolderName = "GymForge-backups";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "gymforge_offsite_" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryLocalSettings _settings = new();
    private readonly ILogger<DatabaseBackupService> _logger = Substitute.For<ILogger<DatabaseBackupService>>();

    public DatabaseBackupOffsiteTests()
    {
        Directory.CreateDirectory(DataDir);
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT); INSERT INTO t(v) VALUES('hola');";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string DataDir => Path.Combine(_root, "data");
    private string ConnectionString => $"Data Source={Path.Combine(DataDir, "gymforge.db")}";
    private string LocalBackupsDir => Path.Combine(DataDir, "backups");
    private string Pendrive => Path.Combine(_root, "pendrive");
    private string OffsiteDir => Path.Combine(Pendrive, OffsiteFolderName);

    private DatabaseBackupService NewService() => new(ConnectionString, _logger, _settings);

    /// <summary>Configura como destino externo un pendrive que sí está enchufado (la carpeta existe).</summary>
    private void PlugInPendrive()
    {
        Directory.CreateDirectory(Pendrive);
        _settings.OffsiteBackupDir = Pendrive;
    }

    private string[] LocalBackups() =>
        Directory.Exists(LocalBackupsDir) ? Directory.GetFiles(LocalBackupsDir, "gymforge-*.db") : [];

    private string[] OffsiteFiles() =>
        Directory.Exists(OffsiteDir) ? Directory.GetFiles(OffsiteDir) : [];

    /// <summary>Deja backups viejos (de 2020) en el destino, el 1 el más antiguo. Devuelve sus nombres.</summary>
    private string[] SeedOldOffsiteBackups(int count)
    {
        Directory.CreateDirectory(OffsiteDir);
        return [.. Enumerable.Range(1, count).Select(i =>
        {
            var name = $"gymforge-20200101-0000{i:00}.db";
            var path = Path.Combine(OffsiteDir, name);
            File.WriteAllText(path, "viejo");
            File.SetLastWriteTime(path, new DateTime(2020, 1, 1).AddDays(i));
            return name;
        })];
    }

    private static string ReadGreeting(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Pooling=False;Mode=ReadOnly");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT v FROM t LIMIT 1";
        return (string)cmd.ExecuteScalar()!;
    }

    private void WarningShouldHaveBeenLogged() =>
        _logger.Received().Log(
            LogLevel.Warning, Arg.Any<EventId>(), Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception?>(), Arg.Any<Func<Arg.AnyType, Exception?, string>>());

    // ── Copia al destino externo ──────────────────────────────────────────────

    [Fact]
    public void BackupNow_ConDestinoExterno_CopiaElUltimoBackup()
    {
        PlugInPendrive();

        NewService().BackupNow("test");

        var localBackup = LocalBackups().Should().ContainSingle().Which;
        var copy = OffsiteFiles().Should().ContainSingle("solo el backup, sin temporales").Which;
        Path.GetFileName(copy).Should().Be(Path.GetFileName(localBackup));
        ReadGreeting(copy).Should().Be("hola", "la copia es una base sana y completa");
    }

    [Fact]
    public void BackupNow_ConExito_ActualizaYGuardaLastOffsiteBackupUtc()
    {
        PlugInPendrive();
        var before = DateTime.UtcNow;

        NewService().BackupNow("test");

        _settings.LastOffsiteBackupUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        _settings.SaveCount.Should().Be(1);
    }

    [Fact]
    public void BackupNow_RotaLasCopiasExternas_AlLimite()
    {
        PlugInPendrive();
        var oldNames = SeedOldOffsiteBackups(12);

        NewService().BackupNow("test");

        var kept = OffsiteFiles().Select(Path.GetFileName).ToList();
        kept.Should().HaveCount(10);
        kept.Should().Contain(Path.GetFileName(LocalBackups().Single()), "la copia nueva es la más reciente");
        kept.Should().NotContain(oldNames.Take(3), "se borran las más viejas");
        kept.Should().Contain(oldNames.Skip(3));
    }

    [Fact]
    public void BackupNow_RotaSoloSusBackups_NoBorraArchivosAjenosDelDestino()
    {
        PlugInPendrive();
        SeedOldOffsiteBackups(12);
        var ajeno = Path.Combine(OffsiteDir, "notas.txt");
        File.WriteAllText(ajeno, "no tocar");

        NewService().BackupNow("test");

        File.Exists(ajeno).Should().BeTrue();
    }

    [Fact]
    public void BackupIfDue_SinBackupDeHoy_TambienCopiaAlDestinoExterno()
    {
        PlugInPendrive();

        NewService().BackupIfDue();

        OffsiteFiles().Should().ContainSingle();
    }

    [Fact]
    public void BackupIfDue_YaHayBackupDeHoy_NoVuelveACopiar()
    {
        PlugInPendrive();
        var service = NewService();
        service.BackupIfDue();

        service.BackupIfDue();

        OffsiteFiles().Should().ContainSingle();
        _settings.SaveCount.Should().Be(1);
    }

    // ── Sin destino / destino caído: best-effort ──────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BackupNow_SinDestinoConfigurado_NoCopiaNiFalla(string? dir)
    {
        _settings.OffsiteBackupDir = dir;

        var backup = () => NewService().BackupNow("test");

        backup.Should().NotThrow();
        LocalBackups().Should().ContainSingle();
        _settings.LastOffsiteBackupUtc.Should().BeNull();
        _settings.SaveCount.Should().Be(0);
    }

    [Fact]
    public void BackupNow_ServicioSinSettings_SoloHaceElBackupLocal()
    {
        var service = new DatabaseBackupService(ConnectionString, _logger);

        var backup = () => service.BackupNow("test");

        backup.Should().NotThrow();
        LocalBackups().Should().ContainSingle();
    }

    [Fact]
    public void BackupNow_DestinoInexistente_NoLanzaConservaElLocalYNoCreaLaCarpeta()
    {
        // Pendrive desenchufado (o letra de unidad tomada por otro disco): la carpeta configurada no está.
        _settings.OffsiteBackupDir = Pendrive;

        var backup = () => NewService().BackupNow("test");

        backup.Should().NotThrow();
        LocalBackups().Should().ContainSingle("el backup local ya quedó hecho");
        Directory.Exists(Pendrive).Should().BeFalse("no se inventan carpetas en un destino que no está");
        _settings.LastOffsiteBackupUtc.Should().BeNull();
        WarningShouldHaveBeenLogged();
    }

    [Fact]
    public void BackupNow_DestinoNoEscribible_NoLanzaYConservaElLocal()
    {
        PlugInPendrive();
        File.WriteAllText(OffsiteDir, "x");   // un archivo ocupa el lugar de la subcarpeta de resguardos

        var backup = () => NewService().BackupNow("test");

        backup.Should().NotThrow();
        LocalBackups().Should().ContainSingle();
        _settings.LastOffsiteBackupUtc.Should().BeNull();
        WarningShouldHaveBeenLogged();
    }

    [Fact]
    public void BackupNow_ConFalloExterno_NoPisaLaFechaDelUltimoResguardoExitoso()
    {
        var lastSuccess = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        _settings.OffsiteBackupDir = Pendrive;   // sin crear: no está enchufado
        _settings.LastOffsiteBackupUtc = lastSuccess;

        NewService().BackupNow("test");

        _settings.LastOffsiteBackupUtc.Should().Be(lastSuccess);
    }

    // ── "Copiar ahora" ────────────────────────────────────────────────────────

    [Fact]
    public void BackupOffsiteNow_ConDestino_CreaUnBackupNuevoYLoCopia()
    {
        PlugInPendrive();
        Directory.CreateDirectory(LocalBackupsDir);
        File.WriteAllText(Path.Combine(LocalBackupsDir, "gymforge-20200101-000000.db"), "de ayer");

        var outcome = NewService().BackupOffsiteNow();

        outcome.Should().Be(OffsiteBackupOutcome.Copied);
        LocalBackups().Should().HaveCount(2, "se creó un backup nuevo, no se reusó el viejo");
        var copy = OffsiteFiles().Should().ContainSingle().Which;
        Path.GetFileName(copy).Should().NotBe("gymforge-20200101-000000.db");
        ReadGreeting(copy).Should().Be("hola");
        _settings.LastOffsiteBackupUtc.Should().NotBeNull();
    }

    [Fact]
    public void BackupOffsiteNow_DestinoNoDisponible_InformaFailedSinLanzar()
    {
        _settings.OffsiteBackupDir = Pendrive;   // no está enchufado

        var outcome = () => NewService().BackupOffsiteNow();

        outcome.Should().NotThrow().Which.Should().Be(OffsiteBackupOutcome.Failed);
        LocalBackups().Should().ContainSingle();
    }

    [Fact]
    public void BackupOffsiteNow_SiNoSePudoCrearElBackupLocal_InformaLocalBackupFailedYNoCopiaNada()
    {
        PlugInPendrive();
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(DataDir, "gymforge.db"));

        var outcome = NewService().BackupOffsiteNow();

        outcome.Should().Be(OffsiteBackupOutcome.LocalBackupFailed);
        OffsiteFiles().Should().BeEmpty();
    }

    [Fact]
    public void BackupOffsiteNow_BarreLosTemporalesPropiosHuerfanos_PeroNoLosAjenos()
    {
        PlugInPendrive();
        Directory.CreateDirectory(OffsiteDir);
        var orphan = Path.Combine(OffsiteDir, "gymforge-20200101-000000.db.tmp");
        var foreign = Path.Combine(OffsiteDir, "otro-programa.tmp");
        File.WriteAllText(orphan, "copia a medias");
        File.WriteAllText(foreign, "no es nuestro");

        NewService().BackupOffsiteNow().Should().Be(OffsiteBackupOutcome.Copied);

        File.Exists(orphan).Should().BeFalse();
        File.Exists(foreign).Should().BeTrue();
    }

    [Fact]
    public void BackupOffsiteNow_SinDestinoConfigurado_InformaNotConfigured()
    {
        NewService().BackupOffsiteNow().Should().Be(OffsiteBackupOutcome.NotConfigured);
    }
}
