using System.Diagnostics;
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
    private readonly InMemoryBackupKeyStore _keys = new(InMemoryBackupKeyStore.TestPassphrase);
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
        foreach (var junction in new[] { OffsiteDir, AliasDir })
            if (new DirectoryInfo(junction).LinkTarget is not null) Directory.Delete(junction);   // solo el junction, no su destino
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string DataDir => Path.Combine(_root, "data");
    private string ConnectionString => $"Data Source={Path.Combine(DataDir, "gymforge.db")}";
    private string LocalBackupsDir => Path.Combine(DataDir, "backups");
    private string AliasDir => Path.Combine(_root, "alias");
    private string Pendrive => Path.Combine(_root, "pendrive");
    private string OffsiteDir => Path.Combine(Pendrive, OffsiteFolderName);

    private DatabaseBackupService NewService(IBackupKeyStore? keys = null) =>
        new(ConnectionString, _logger, _settings, keys ?? _keys);

    /// <summary>Configura como destino externo un pendrive que sí está enchufado (la carpeta existe).</summary>
    private void PlugInPendrive()
    {
        Directory.CreateDirectory(Pendrive);
        _settings.OffsiteBackupDir = Pendrive;
    }

    /// <summary>La copia externa corre en segundo plano: se espera para poder afirmar sobre su resultado.</summary>
    private bool BackupNowAndWait()
    {
        var service = NewService();
        var localOk = service.BackupNow("test");
        service.PendingOffsiteCopy.Wait();
        return localOk;
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
            var name = $"gymforge-20200101-0000{i:00}.db.gfbak";
            var path = Path.Combine(OffsiteDir, name);
            File.WriteAllText(path, "viejo");
            File.SetLastWriteTime(path, new DateTime(2020, 1, 1).AddDays(i));
            return name;
        })];
    }

    /// <summary>Descifra una copia externa con la frase de prueba y la deja como base legible junto a ella.</summary>
    private static string ReadOffsiteGreeting(string gfbakPath)
    {
        var dbPath = gfbakPath + ".descifrada.db";
        using (var cipher = File.OpenRead(gfbakPath))
        using (var plain = File.Create(dbPath))
            BackupCrypto.Decrypt(cipher, plain, InMemoryBackupKeyStore.TestPassphrase);
        return ReadGreeting(dbPath);
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

        BackupNowAndWait();

        var localBackup = LocalBackups().Should().ContainSingle().Which;
        var copy = OffsiteFiles().Should().ContainSingle("solo el backup, sin temporales").Which;
        Path.GetFileName(copy).Should().Be(Path.GetFileName(localBackup) + ".gfbak");
        ReadOffsiteGreeting(copy).Should().Be("hola", "la copia es una base sana y completa");
    }

    [Fact]
    public void BackupNow_ConExito_ActualizaYGuardaLastOffsiteBackupUtc()
    {
        PlugInPendrive();
        var before = DateTime.UtcNow;

        BackupNowAndWait();

        _settings.LastOffsiteBackupUtc.Should().BeOnOrAfter(before).And.BeOnOrBefore(DateTime.UtcNow);
        _settings.SaveCount.Should().Be(1);
    }

    [Fact]
    public void BackupNow_RotaLasCopiasExternas_AlLimite()
    {
        PlugInPendrive();
        var oldNames = SeedOldOffsiteBackups(12);

        BackupNowAndWait();

        var kept = OffsiteFiles().Select(Path.GetFileName).ToList();
        kept.Should().HaveCount(10);
        kept.Should().Contain(Path.GetFileName(LocalBackups().Single()) + ".gfbak", "la copia nueva es la más reciente");
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

        BackupNowAndWait();

        File.Exists(ajeno).Should().BeTrue();
    }

    [Fact]
    public void BackupIfDue_SinBackupDeHoy_TambienCopiaAlDestinoExterno()
    {
        PlugInPendrive();

        var service = NewService();
        service.BackupIfDue();
        service.PendingOffsiteCopy.Wait();

        OffsiteFiles().Should().ContainSingle();
    }

    [Fact]
    public void BackupIfDue_YaHayBackupDeHoy_NoVuelveACopiar()
    {
        PlugInPendrive();
        var service = NewService();
        service.BackupIfDue();
        service.PendingOffsiteCopy.Wait();

        service.BackupIfDue();
        service.PendingOffsiteCopy.Wait();

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

        var backup = () => BackupNowAndWait();

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

        var backup = () => BackupNowAndWait();

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

        var backup = () => BackupNowAndWait();

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

        BackupNowAndWait();

        _settings.LastOffsiteBackupUtc.Should().Be(lastSuccess);
    }

    [Fact]
    public void BackupNow_ConDestinoCaido_DevuelveTrueDelLocalYNoPropagaNada()
    {
        _settings.OffsiteBackupDir = Pendrive;   // no está enchufado
        var service = NewService();

        service.BackupNow("test").Should().BeTrue();

        service.PendingOffsiteCopy.Wait();
        service.PendingOffsiteCopy.IsFaulted.Should().BeFalse();
    }

    [Fact]
    public void BackupNow_SiLaCopiaExternaLanza_NoFallaYLoLoguea()
    {
        PlugInPendrive();
        var settings = Substitute.For<ILocalSettings>();
        settings.OffsiteBackupDir.Returns(Pendrive);
        settings.When(x => x.Save()).Do(_ => throw new InvalidOperationException("boom"));
        var service = new DatabaseBackupService(ConnectionString, _logger, settings, _keys);

        service.BackupNow("test").Should().BeTrue();

        service.PendingOffsiteCopy.Wait();
        service.PendingOffsiteCopy.IsFaulted.Should().BeFalse();
        WarningShouldHaveBeenLogged();
    }

    [Fact]
    public void BackupNow_SinBaseLocal_DevuelveFalse()
    {
        PlugInPendrive();
        SqliteConnection.ClearAllPools();
        File.Delete(Path.Combine(DataDir, "gymforge.db"));

        BackupNowAndWait().Should().BeFalse();
        OffsiteFiles().Should().BeEmpty();
    }

    // ── Cifrado de la copia externa (ADR-012) ─────────────────────────────────

    [Fact]
    public void BackupNow_ConFrase_LaCopiaExternaEsGfbkYNoUnSqliteLegible()
    {
        PlugInPendrive();

        BackupNowAndWait();

        var copy = OffsiteFiles().Should().ContainSingle().Which;
        copy.Should().EndWith(".db.gfbak");
        var bytes = File.ReadAllBytes(copy);
        bytes.Take(4).Should().Equal("GFBK"u8.ToArray());
        System.Text.Encoding.Latin1.GetString(bytes).Should()
            .NotContain("SQLite format 3").And.NotContain("hola", "el contenido de la base no queda a la vista");
    }

    [Fact]
    public void BackupOffsiteNow_SinFrase_NoEscribeNadaEnClaroEInformaMissingPassphrase()
    {
        PlugInPendrive();

        var outcome = NewService(new InMemoryBackupKeyStore()).BackupOffsiteNow();

        outcome.Should().Be(OffsiteBackupOutcome.MissingPassphrase);
        OffsiteFiles().Should().BeEmpty("sin frase nunca sale una base en claro");
        Directory.Exists(OffsiteDir).Should().BeFalse();
        LocalBackups().Should().ContainSingle("el backup local sí queda hecho, en claro");
        _settings.LastOffsiteBackupUtc.Should().BeNull();
    }

    [Fact]
    public void BackupNow_SinFrase_NoLanzaNiCopiaNada()
    {
        PlugInPendrive();
        var service = NewService(new InMemoryBackupKeyStore());

        service.BackupNow("test").Should().BeTrue();

        service.PendingOffsiteCopy.Wait();
        service.PendingOffsiteCopy.IsFaulted.Should().BeFalse();
        OffsiteFiles().Should().BeEmpty();
    }

    [Fact]
    public void BackupNow_ServicioSinKeyStore_NoCopiaNadaAfuera()
    {
        PlugInPendrive();
        var service = new DatabaseBackupService(ConnectionString, _logger, _settings);

        service.BackupOffsiteNow().Should().Be(OffsiteBackupOutcome.MissingPassphrase);

        OffsiteFiles().Should().BeEmpty();
    }

    [Fact]
    public void BackupNow_RotaSoloLasCopiasCifradas_NoTocaLasBasesEnClaroViejas()
    {
        PlugInPendrive();
        Directory.CreateDirectory(OffsiteDir);
        var enClaro = Path.Combine(OffsiteDir, "gymforge-20190101-000000.db");
        File.WriteAllText(enClaro, "copia vieja de antes del cifrado");
        SeedOldOffsiteBackups(12);

        BackupNowAndWait();

        File.Exists(enClaro).Should().BeTrue("la rotación solo maneja *.gfbak; borrar archivos ajenos no es su trabajo");
        OffsiteFiles().Count(f => f.EndsWith(".gfbak")).Should().Be(10);
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
        Path.GetFileName(copy).Should().NotBe("gymforge-20200101-000000.db.gfbak");
        ReadOffsiteGreeting(copy).Should().Be("hola");
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
        var orphan = Path.Combine(OffsiteDir, "gymforge-20200101-000000.db.gfbak.tmp");
        var legacyOrphan = Path.Combine(OffsiteDir, "gymforge-20190101-000000.db.tmp");
        var foreign = Path.Combine(OffsiteDir, "otro-programa.tmp");
        File.WriteAllText(orphan, "copia a medias");
        File.WriteAllText(legacyOrphan, "copia a medias de una versión anterior");
        File.WriteAllText(foreign, "no es nuestro");

        NewService().BackupOffsiteNow().Should().Be(OffsiteBackupOutcome.Copied);

        File.Exists(orphan).Should().BeFalse();
        File.Exists(legacyOrphan).Should().BeFalse();
        File.Exists(foreign).Should().BeTrue();
    }

    [Fact]
    public void BackupOffsiteNow_SinDestinoConfigurado_InformaNotConfigured()
    {
        NewService().BackupOffsiteNow().Should().Be(OffsiteBackupOutcome.NotConfigured);
    }

    // ── copias en claro previas al cifrado (NEW-52) ─────────────────────────

    private string[] SeedLegacyAndBystanders()
    {
        PlugInPendrive();
        Directory.CreateDirectory(OffsiteDir);
        string[] legacy = ["gymforge-20240101-101010.db", "gymforge-20240102-101010-cierre-de-caja.db"];
        string[] bystanders =
        [
            "gymforge-20240101-101010.db.gfbak", "gymforge-20240101-101010.db.tmp",
            "gymforge-20240101-101010.db.bak", "gymforge-notas.db", "otro.db", "Gymforge-20240101-101010.dbx",
        ];
        foreach (var name in legacy.Concat(bystanders)) File.WriteAllText(Path.Combine(OffsiteDir, name), "x");
        File.WriteAllText(Path.Combine(Pendrive, "gymforge-20240103-101010.db"), "fuera de la subcarpeta");
        return legacy;
    }

    [Fact]
    public void FindLegacyPlainOffsiteCopies_CuentaSoloLasCopiasEnClaroLegadas()
    {
        var legacy = SeedLegacyAndBystanders();

        NewService().FindLegacyPlainOffsiteCopies().Should().BeEquivalentTo(legacy);
    }

    [Fact]
    public void FindLegacyPlainOffsiteCopies_SinDestinoODestinoAusente_DevuelveVacio()
    {
        NewService().FindLegacyPlainOffsiteCopies().Should().BeEmpty();

        _settings.OffsiteBackupDir = Path.Combine(_root, "no-esta");
        NewService().FindLegacyPlainOffsiteCopies().Should().BeEmpty();
    }

    [Fact]
    public void DeleteLegacyPlainOffsiteCopies_BorraExactamenteLasLegadas_YNadaMas()
    {
        var legacy = SeedLegacyAndBystanders();
        var before = Directory.GetFiles(OffsiteDir).Select(Path.GetFileName).ToList();

        var deleted = NewService().DeleteLegacyPlainOffsiteCopies();

        deleted.Should().BeEquivalentTo(legacy);
        Directory.GetFiles(OffsiteDir).Select(Path.GetFileName)
            .Should().BeEquivalentTo(before.Except(legacy));
        File.Exists(Path.Combine(Pendrive, "gymforge-20240103-101010.db")).Should().BeTrue();
    }

    [Fact]
    public void LegacySweep_NoTocaLosBackupsLocales()
    {
        SeedLegacyAndBystanders();
        var service = NewService();
        service.BackupNow("test");
        service.PendingOffsiteCopy.Wait();
        var localBefore = Directory.GetFiles(LocalBackupsDir);

        service.DeleteLegacyPlainOffsiteCopies();

        Directory.GetFiles(LocalBackupsDir).Should().BeEquivalentTo(localBefore);
        OffsiteFiles().Should().NotBeEmpty();
    }

    // ── guard contra alias de la carpeta local (NEW-69) ─────────────────────

    /// <summary>Crea un junction (no requiere privilegios en Windows) de <paramref name="link"/> hacia <paramref name="target"/>.</summary>
    private static void CreateJunction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        mklink.ExitCode.Should().Be(0, "el test necesita un junction real hacia la carpeta de backups locales");
    }

    [Fact]
    public void LegacySweep_OffsiteQueEsJunctionDeLaCarpetaLocal_NoDetectaNiBorraNada()
    {
        // Los backups locales matchean la regex de las copias legadas: si el guard no resolviera el alias, se borrarían.
        Directory.CreateDirectory(LocalBackupsDir);
        var localBackup = Path.Combine(LocalBackupsDir, "gymforge-20240101-101010.db");
        File.WriteAllText(localBackup, "backup local");
        PlugInPendrive();
        CreateJunction(OffsiteDir, LocalBackupsDir);
        var service = NewService();

        service.FindLegacyPlainOffsiteCopies().Should().BeEmpty();
        service.DeleteLegacyPlainOffsiteCopies().Should().BeEmpty();

        File.Exists(localBackup).Should().BeTrue();
    }

    [Fact]
    public void LegacySweep_JunctionCuyoDestinoPasaPorOtroJunctionEnUnAncestro_NoDetectaNiBorraNada()
    {
        // offsite → alias\backups, y "alias" es a su vez un junction de la carpeta de datos: el destino del link
        // solo coincide con la carpeta local si también se normalizan los ancestros del destino (NEW-72).
        Directory.CreateDirectory(LocalBackupsDir);
        var localBackup = Path.Combine(LocalBackupsDir, "gymforge-20240101-101010.db");
        File.WriteAllText(localBackup, "backup local");
        CreateJunction(AliasDir, DataDir);
        PlugInPendrive();
        CreateJunction(OffsiteDir, Path.Combine(AliasDir, "backups"));
        var service = NewService();

        service.FindLegacyPlainOffsiteCopies().Should().BeEmpty();
        service.DeleteLegacyPlainOffsiteCopies().Should().BeEmpty();

        File.Exists(localBackup).Should().BeTrue();
    }

    // Cobertura: el caso de igualdad directa de rutas no es alcanzable (la subcarpeta externa se llama
    // "GymForge-backups" y la local "backups"); el alias por junction es la forma real de que coincidan y la cubre
    // el test de arriba. Los symlinks usan la misma resolución (ResolveLinkTarget) pero crearlos exige privilegios;
    // y si la resolución falla (IOException/UnauthorizedAccessException) el catch existente devuelve vacío: no se barre.
}
