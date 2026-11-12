using FluentAssertions;
using GymForge.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace GymForge.Integration.Tests;

/// <summary>Frase de acceso del respaldo guardada con DPAPI (usuario actual): no queda en claro en disco.</summary>
public sealed class DpapiBackupKeyStoreTests : IDisposable
{
    private const string Pass = "frase secreta bien larga";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "gymforge_keystore_" + Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_root, "vendor", "backup-pass.dpapi");

    private DpapiBackupKeyStore NewStore() => new(Substitute.For<ILogger<DpapiBackupKeyStore>>(), FilePath);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void SinFraseFijada_NoHayFraseYGetDevuelveNull()
    {
        var store = NewStore();

        store.HasPassphrase.Should().BeFalse();
        store.GetPassphrase().Should().BeNull();
    }

    [Fact]
    public void SetLuegoGet_DevuelveLaMismaFrase_YSobreviveAUnaInstanciaNueva()
    {
        NewStore().SetPassphrase(Pass);

        var other = NewStore();

        other.HasPassphrase.Should().BeTrue();
        other.GetPassphrase().Should().Be(Pass);
    }

    [Fact]
    public void LoQueQuedaEnDisco_NoEsLaFraseEnClaro()
    {
        NewStore().SetPassphrase(Pass);

        var onDisk = File.ReadAllBytes(FilePath);

        System.Text.Encoding.UTF8.GetString(onDisk).Should().NotContain(Pass);
        System.Text.Encoding.Unicode.GetString(onDisk).Should().NotContain(Pass);
        onDisk.Should().NotEqual(System.Text.Encoding.UTF8.GetBytes(Pass));
    }

    [Fact]
    public void CambiarLaFrase_ReemplazaLaAnterior()
    {
        var store = NewStore();
        store.SetPassphrase(Pass);

        store.SetPassphrase("otra frase igual de larga");

        store.GetPassphrase().Should().Be("otra frase igual de larga");
    }

    [Fact]
    public void FraseDemasiadoCorta_SeRechazaYNoSeGuardaNada()
    {
        var store = NewStore();

        var act = () => store.SetPassphrase("corta");

        act.Should().Throw<ArgumentException>();
        store.HasPassphrase.Should().BeFalse();
    }

    [Fact]
    public void SetPassphrase_RecortaLosEspaciosDeLosBordes()
    {
        NewStore().SetPassphrase("  " + Pass + " ");

        NewStore().GetPassphrase().Should().Be(Pass);
    }

    [Fact]
    public void FraseQueSoloAlcanzaLaLongitudConEspacios_SeRechaza()
    {
        var store = NewStore();

        var act = () => store.SetPassphrase("corta" + new string(' ', 20));

        act.Should().Throw<ArgumentException>();
        store.HasPassphrase.Should().BeFalse();
    }

    [Fact]
    public void ArchivoDanado_SeTrataComoSinFraseSinLanzar()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath, [1, 2, 3, 4]);

        var act = () => NewStore().GetPassphrase();

        act.Should().NotThrow().Which.Should().BeNull();
    }
}
