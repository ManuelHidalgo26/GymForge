using System.IO.Compression;
using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>
/// Migración de datos entre PCs: exportar a un zip e importarlo en otra instalación.
/// El caso que importa es el round-trip completo, incluida la reescritura de las rutas
/// de logo y fotos (en la PC nueva el perfil de usuario es otro y las rutas absolutas
/// que guarda la base no existen).
/// </summary>
public class DataTransferServiceTests : IDisposable
{
    private const string Pass = InMemoryBackupKeyStore.TestPassphrase;

    private readonly List<string> _dirs = [];

    [Fact]
    public async Task Export_WritesPackage_WithDatabaseManifestAndFiles()
    {
        var gym = NewInstall("Iron Temple", members: 3, payments: 2, withLogo: true);
        var package = Path.Combine(NewDir(), "export.gfbak");

        var info = await gym.Service.ExportAsync(package);

        File.Exists(package).Should().BeTrue();
        info.GymName.Should().Be("Iron Temple");
        info.Members.Should().Be(3);
        info.Payments.Should().Be(2);
        info.HasLogo.Should().BeTrue();

        using var archive = ZipFile.OpenRead(DecryptToZip(package));
        archive.GetEntry("manifest.json").Should().NotBeNull();
        archive.GetEntry("gymforge.db").Should().NotBeNull();
        archive.Entries.Should().Contain(e => e.FullName.Contains("brand/") && e.Name == "logo.png");
    }

    [Fact]
    public async Task Export_DoesNotCarryFilesThatBelongToThisPc_SettingsBackupsVendorOrLogs()
    {
        // settings.json guarda el destino del backup externo (F:\ de ESTA PC): en otra PC apuntaría a otro disco.
        var gym = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        File.WriteAllText(Path.Combine(gym.DataDir, "settings.json"), """{"OffsiteBackupDir":"F:\\Respaldos"}""");
        foreach (var carpeta in new[] { "backups", "vendor", "logs", "recibos" })
        {
            Directory.CreateDirectory(Path.Combine(gym.DataDir, carpeta));
            File.WriteAllText(Path.Combine(gym.DataDir, carpeta, "propio-de-esta-pc.txt"), "x");
        }
        File.WriteAllText(Path.Combine(gym.DataDir, "vendor", "backup-pass.dpapi"), "frase protegida");
        var package = Path.Combine(NewDir(), "export.gfbak");

        await gym.Service.ExportAsync(package);

        using var archive = ZipFile.OpenRead(DecryptToZip(package));
        archive.Entries.Select(e => e.FullName).Should().NotContain(
            name => name.Contains("settings.json") || name.Contains("propio-de-esta-pc") || name.Contains("backup-pass"),
            "solo viajan la base, el logo y las fotos");
    }

    [Fact]
    public async Task Import_KeepsTheSettingsOfThePcItRunsOn()
    {
        var origen = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var zip = Path.Combine(NewDir(), "mudanza.gfbak");
        await origen.Service.ExportAsync(zip);

        var destino = NewInstall("Otro Gym", members: 0, payments: 0, withLogo: false, passphrase: null);
        var settingsPath = Path.Combine(destino.DataDir, "settings.json");
        File.WriteAllText(settingsPath, """{"OffsiteBackupDir":"G:\\Mi-Pendrive"}""");

        await destino.Service.ImportAsync(zip, Pass);

        File.ReadAllText(settingsPath).Should().Contain("G:\\\\Mi-Pendrive");
    }

    [Fact]
    public async Task ExportThenImport_OnAnotherInstall_RestoresDataAndRewritesPaths()
    {
        // PC vieja: 3 socios, uno con foto, y logo del gimnasio.
        var origen = NewInstall("Iron Temple", members: 3, payments: 2, withLogo: true);
        AddMemberPhoto(origen, "socio-1.jpg");

        var zip = Path.Combine(NewDir(), "mudanza.gfbak");
        await origen.Service.ExportAsync(zip);

        // PC nueva: instalación limpia, con otro perfil de usuario y SIN la caché DPAPI de la vieja: solo la frase.
        var destino = NewInstall("Otro Gym", members: 0, payments: 0, withLogo: false, passphrase: null);

        await destino.Service.ImportAsync(zip, Pass);

        // Los datos son los del paquete, no los que había.
        Query(destino.ConnectionString, "SELECT LegalName FROM Companies LIMIT 1").Should().Be("Iron Temple");
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("3");

        // Los archivos llegaron.
        File.Exists(Path.Combine(destino.DataDir, "brand", "logo.png")).Should().BeTrue();
        File.Exists(Path.Combine(destino.DataDir, "fotos", "socio-1.jpg")).Should().BeTrue();

        // Y las rutas de la base apuntan a la carpeta de ESTA PC, no a la de la otra.
        var logo = Query(destino.ConnectionString, "SELECT LogoUrl FROM Companies LIMIT 1")!;
        logo.Should().Be(Path.Combine(destino.DataDir, "brand", "logo.png"));
        logo.Should().NotContain(origen.DataDir);

        var foto = Query(destino.ConnectionString, "SELECT PhotoUrl FROM Members WHERE PhotoUrl IS NOT NULL LIMIT 1")!;
        foto.Should().Be(Path.Combine(destino.DataDir, "fotos", "socio-1.jpg"));
    }

    [Fact]
    public async Task Import_BacksUpCurrentDatabase_BeforeReplacing()
    {
        var origen = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var zip = Path.Combine(NewDir(), "export.gfbak");
        await origen.Service.ExportAsync(zip);

        var destino = NewInstall("Gimnasio Actual", members: 7, payments: 0, withLogo: false);

        await destino.Service.ImportAsync(zip, Pass);

        destino.Backup.Received().BackupNow(Arg.Is<string>(r => r.Contains("import")));
    }

    [Fact]
    public async Task Inspect_Rejects_FileThatIsNotAGymForgePackage()
    {
        var gym = NewInstall("Iron Temple", members: 0, payments: 0, withLogo: false);
        var zip = Path.Combine(NewDir(), "cualquiera.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("hola.txt");

        var act = () => gym.Service.InspectAsync(zip);

        (await act.Should().ThrowAsync<DataPackageException>())
            .WithMessage("*no es una exportación de GymForge*");
    }

    [Fact]
    public async Task Inspect_Rejects_PackageFromANewerVersion()
    {
        var gym = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var zip = ExportLegacyZip(gym, "futuro.zip");
        RewriteManifest(zip, """
            {"FormatVersion":99,"AppVersion":"9.9.9","GymName":"Iron Temple",
             "ExportedAt":"2030-01-01T00:00:00","Members":1,"Payments":0,
             "HasLogo":false,"Photos":0}
            """);

        var act = () => gym.Service.InspectAsync(zip);

        (await act.Should().ThrowAsync<DataPackageException>())
            .WithMessage("*versión más nueva*9.9.9*");
    }

    [Fact]
    public async Task Inspect_Rejects_PackageWithCorruptDatabase()
    {
        var gym = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var zip = ExportLegacyZip(gym, "corrupto.zip");
        ReplaceEntry(zip, "gymforge.db", "esto no es una base sqlite valida");

        var act = () => gym.Service.InspectAsync(zip);

        (await act.Should().ThrowAsync<DataPackageException>())
            .WithMessage("*dañada*");
    }

    [Fact]
    public async Task Import_LeavesCurrentDataUntouched_WhenPackageIsInvalid()
    {
        var destino = NewInstall("Gimnasio Actual", members: 7, payments: 0, withLogo: false);
        var zip = Path.Combine(NewDir(), "basura.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            archive.CreateEntry("hola.txt");

        var act = () => destino.Service.ImportAsync(zip);

        await act.Should().ThrowAsync<DataPackageException>();
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("7");
        Query(destino.ConnectionString, "SELECT LegalName FROM Companies LIMIT 1").Should().Be("Gimnasio Actual");
    }

    // ── cifrado (ADR-012) ───────────────────────────────────────────────────

    [Fact]
    public async Task Export_SalePorCompletoComoGfbak_NoComoZip()
    {
        var gym = NewInstall("Iron Temple", members: 2, payments: 0, withLogo: true);
        var package = Path.Combine(NewDir(), "export.gfbak");

        await gym.Service.ExportAsync(package);

        var bytes = File.ReadAllBytes(package);
        bytes.Take(4).Should().Equal("GFBK"u8.ToArray());
        System.Text.Encoding.Latin1.GetString(bytes).Should().NotContain("Iron Temple").And.NotContain("manifest.json");
        gym.Service.IsEncrypted(package).Should().BeTrue();
        File.Exists(package + ".tmp").Should().BeFalse("el temporal se renombra al terminar");
    }

    [Fact]
    public async Task Export_SinFrase_EstaBloqueadoYNoDejaNingunArchivo()
    {
        var gym = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false, passphrase: null);
        var package = Path.Combine(NewDir(), "export.gfbak");

        var act = () => gym.Service.ExportAsync(package);

        (await act.Should().ThrowAsync<DataPackageException>()).WithMessage("*frase de acceso*");
        File.Exists(package).Should().BeFalse("un paquete en claro no debe salir de la PC");
    }

    [Fact]
    public async Task Inspect_Cifrado_ConLaFrase_DevuelveElContenido()
    {
        var gym = NewInstall("Iron Temple", members: 3, payments: 1, withLogo: false);
        var package = Path.Combine(NewDir(), "export.gfbak");
        await gym.Service.ExportAsync(package);

        var info = await gym.Service.InspectAsync(package, Pass);

        info.GymName.Should().Be("Iron Temple");
        info.Members.Should().Be(3);
    }

    [Fact]
    public async Task Inspect_Cifrado_SinFrase_PideLaFrase()
    {
        var gym = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var package = Path.Combine(NewDir(), "export.gfbak");
        await gym.Service.ExportAsync(package);

        var act = () => gym.Service.InspectAsync(package);

        (await act.Should().ThrowAsync<DataPackageException>()).WithMessage("*cifrado*frase*");
    }

    [Fact]
    public async Task Import_ConFraseIncorrecta_FallaConMensajeClaroYNoTocaLosDatos()
    {
        var origen = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var package = Path.Combine(NewDir(), "export.gfbak");
        await origen.Service.ExportAsync(package);
        var destino = NewInstall("Gimnasio Actual", members: 7, payments: 0, withLogo: false);

        var act = () => destino.Service.ImportAsync(package, "otra frase distinta y larga");

        (await act.Should().ThrowAsync<DataPackageException>())
            .WithMessage("La frase de acceso es incorrecta, o el archivo está dañado.");
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("7");
        Query(destino.ConnectionString, "SELECT LegalName FROM Companies LIMIT 1").Should().Be("Gimnasio Actual");
        destino.Backup.DidNotReceive().BackupNow(Arg.Any<string>());

        await destino.Service.ImportAsync(package, Pass);   // y permite reintentar con la correcta
        Query(destino.ConnectionString, "SELECT LegalName FROM Companies LIMIT 1").Should().Be("Iron Temple");
    }

    [Fact]
    public async Task Import_ZipLegacySinCifrar_SigueImportandose()
    {
        var origen = NewInstall("Iron Temple", members: 3, payments: 0, withLogo: true);
        var zip = ExportLegacyZip(origen, "viejo.zip");
        var destino = NewInstall("Otro Gym", members: 0, payments: 0, withLogo: false, passphrase: null);

        destino.Service.IsEncrypted(zip).Should().BeFalse();
        await destino.Service.ImportAsync(zip);

        Query(destino.ConnectionString, "SELECT LegalName FROM Companies LIMIT 1").Should().Be("Iron Temple");
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("3");
    }

    [Fact]
    public async Task Import_ConUnByteAlterado_FallaYNoTocaLosDatos()
    {
        var origen = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var package = Path.Combine(NewDir(), "export.gfbak");
        await origen.Service.ExportAsync(package);
        var bytes = File.ReadAllBytes(package);
        bytes[bytes.Length / 2] ^= 0x01;
        File.WriteAllBytes(package, bytes);
        var destino = NewInstall("Gimnasio Actual", members: 7, payments: 0, withLogo: false);

        var act = () => destino.Service.ImportAsync(package, Pass);

        await act.Should().ThrowAsync<DataPackageException>();
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("7");
    }

    [Fact]
    public async Task Import_ConElArchivoTruncado_FallaYNoTocaLosDatos()
    {
        var origen = NewInstall("Iron Temple", members: 1, payments: 0, withLogo: false);
        var package = Path.Combine(NewDir(), "export.gfbak");
        await origen.Service.ExportAsync(package);
        var bytes = File.ReadAllBytes(package);
        File.WriteAllBytes(package, bytes[..^20]);
        var destino = NewInstall("Gimnasio Actual", members: 7, payments: 0, withLogo: false);

        var act = () => destino.Service.ImportAsync(package, Pass);

        await act.Should().ThrowAsync<DataPackageException>();
        Query(destino.ConnectionString, "SELECT COUNT(*) FROM Members").Should().Be("7");
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed record Install(
        DataTransferService Service, string ConnectionString, string DataDir, IDatabaseBackup Backup);

    /// <summary>Exporta (cifrado) y descifra a un .zip: el formato de los paquetes viejos, para probar la compatibilidad de lectura.</summary>
    private string ExportLegacyZip(Install gym, string fileName)
    {
        var package = Path.Combine(NewDir(), "export.gfbak");
        gym.Service.ExportAsync(package).GetAwaiter().GetResult();
        var zip = Path.Combine(NewDir(), fileName);
        File.Copy(DecryptToZip(package), zip);
        return zip;
    }

    private string DecryptToZip(string package)
    {
        var zip = Path.Combine(NewDir(), "descifrado.zip");
        using var cipher = File.OpenRead(package);
        using var plain = File.Create(zip);
        BackupCrypto.Decrypt(cipher, plain, Pass);
        return zip;
    }

    /// <summary>Simula una instalación de GymForge: su carpeta de datos y su base. <paramref name="passphrase"/> null = sin frase fijada.</summary>
    private Install NewInstall(string gym, int members, int payments, bool withLogo, string? passphrase = Pass)
    {
        var dataDir = NewDir();
        var dbPath = Path.Combine(dataDir, "gymforge.db");
        var connStr = $"Data Source={dbPath}";

        using (var c = new SqliteConnection(connStr))
        {
            c.Open();
            Exec(c, """
                CREATE TABLE Companies(Id TEXT PRIMARY KEY, LegalName TEXT, LogoUrl TEXT);
                CREATE TABLE Members(Id TEXT PRIMARY KEY, PhotoUrl TEXT);
                CREATE TABLE Payments(Id TEXT PRIMARY KEY);
                CREATE TABLE BodyMeasurements(Id TEXT PRIMARY KEY, PhotoFront TEXT, PhotoSide TEXT, PhotoBack TEXT);
                """);

            var logo = withLogo ? Path.Combine(dataDir, "brand", "logo.png") : null;
            Exec(c, "INSERT INTO Companies(Id, LegalName, LogoUrl) VALUES($id, $n, $l)",
                ("$id", Guid.NewGuid().ToString()), ("$n", gym), ("$l", (object?)logo ?? DBNull.Value));

            for (var i = 0; i < members; i++)
                Exec(c, "INSERT INTO Members(Id, PhotoUrl) VALUES($id, NULL)", ("$id", Guid.NewGuid().ToString()));
            for (var i = 0; i < payments; i++)
                Exec(c, "INSERT INTO Payments(Id) VALUES($id)", ("$id", Guid.NewGuid().ToString()));
        }
        SqliteConnection.ClearAllPools();

        if (withLogo)
        {
            Directory.CreateDirectory(Path.Combine(dataDir, "brand"));
            File.WriteAllText(Path.Combine(dataDir, "brand", "logo.png"), "png falso");
        }

        var backup = Substitute.For<IDatabaseBackup>();
        var svc = new DataTransferService(
            connStr, backup, new InMemoryBackupKeyStore(passphrase), Substitute.For<ILogger<DataTransferService>>());
        return new Install(svc, connStr, dataDir, backup);
    }

    private static void AddMemberPhoto(Install install, string fileName)
    {
        var fotos = Path.Combine(install.DataDir, "fotos");
        Directory.CreateDirectory(fotos);
        File.WriteAllText(Path.Combine(fotos, fileName), "jpg falso");

        using var c = new SqliteConnection(install.ConnectionString);
        c.Open();
        Exec(c, "UPDATE Members SET PhotoUrl = $p WHERE Id = (SELECT Id FROM Members LIMIT 1)",
            ("$p", Path.Combine(fotos, fileName)));
        SqliteConnection.ClearAllPools();
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static string? Query(string connStr, string sql)
    {
        using var c = new SqliteConnection(connStr);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var result = cmd.ExecuteScalar();
        SqliteConnection.ClearAllPools();
        return result is null or DBNull ? null : result.ToString();
    }

    private static void RewriteManifest(string zip, string json)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
        archive.GetEntry("manifest.json")!.Delete();
        using var w = new StreamWriter(archive.CreateEntry("manifest.json").Open());
        w.Write(json);
    }

    private static void ReplaceEntry(string zip, string entry, string content)
    {
        using var archive = ZipFile.Open(zip, ZipArchiveMode.Update);
        archive.GetEntry(entry)!.Delete();
        using var w = new StreamWriter(archive.CreateEntry(entry).Open());
        w.Write(content);
    }

    private string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gymforge_transfer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        return dir;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var dir in _dirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* limpieza best-effort */ }
        }
    }
}
