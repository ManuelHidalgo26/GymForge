using FluentAssertions;
using GymForge.Application.UseCases.Licensing;
using GymForge.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace GymForge.Integration.Tests;

public class WindowsMachineFingerprintTests
{
    private static WindowsMachineFingerprint WithSources(params Func<string?>[] sources) =>
        new(NullLogger<WindowsMachineFingerprint>.Instance, sources);

    // ── Fuentes reales (la app y el CI son Windows) ──────────────────────────

    [Fact]
    public void Current_EnEstaPc_DevuelveUnCodigoLegible()
    {
        var code = new WindowsMachineFingerprint(NullLogger<WindowsMachineFingerprint>.Instance).Current();

        MachineCode.TryNormalize(code, out var normalized).Should().BeTrue($"'{code}' debería ser XXXX-XXXX-XXXX-XXXX");
        normalized.Should().Be(code);
    }

    [Fact]
    public void Current_EnEstaPc_EsEstableEntreInstancias()
    {
        var first = new WindowsMachineFingerprint(NullLogger<WindowsMachineFingerprint>.Instance).Current();
        var second = new WindowsMachineFingerprint(NullLogger<WindowsMachineFingerprint>.Instance).Current();

        second.Should().Be(first);
    }

    // ── Cadena de fuentes ────────────────────────────────────────────────────

    [Fact]
    public void Current_DerivaElCodigoDeLaPrimeraFuenteDisponible()
    {
        var sut = WithSources(() => "machine-guid", () => "volume-serial");

        sut.Current().Should().Be(MachineCode.FromSource("machine-guid"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Current_FuenteSinValor_PasaALaSiguiente(string? emptyValue)
    {
        var sut = WithSources(() => emptyValue, () => "volume-serial");

        sut.Current().Should().Be(MachineCode.FromSource("volume-serial"));
    }

    [Fact]
    public void Current_FuenteQueLanza_PasaALaSiguienteSinPropagar()
    {
        var sut = WithSources(
            () => throw new UnauthorizedAccessException("sin permiso al registro"),
            () => "volume-serial");

        sut.Current().Should().Be(MachineCode.FromSource("volume-serial"));
    }

    [Fact]
    public void Current_NingunaFuenteDisponible_DevuelveVacioSinLanzar()
    {
        var sut = WithSources(() => null, () => throw new InvalidOperationException("sin disco"));

        var current = sut.Current;

        current.Should().NotThrow().Which.Should().BeEmpty();
    }

    [Fact]
    public void Current_PrimeraFuenteDisponible_NoConsultaLasSiguientes()
    {
        var consulted = 0;
        var sut = WithSources(() => "machine-guid", () => { consulted++; return "volume-serial"; });

        sut.Current();

        consulted.Should().Be(0);
    }

    [Fact]
    public void Current_SeCalculaUnaSolaVez()
    {
        var reads = 0;
        var sut = WithSources(() => { reads++; return "machine-guid"; });

        sut.Current();
        sut.Current();

        reads.Should().Be(1);
    }
}
