using System.Text.RegularExpressions;
using FluentAssertions;
using GymForge.Application.UseCases.Licensing;

namespace GymForge.Application.Tests.Licensing;

public partial class MachineCodeTests
{
    [GeneratedRegex("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$")]
    private static partial Regex CodeShape();

    [Fact]
    public void FromSource_DevuelveCuatroGruposDeCuatroHexEnMayusculas()
    {
        var code = MachineCode.FromSource("0d2c7f3e-5a1b-4c9d-8e6f-1a2b3c4d5e6f");

        CodeShape().IsMatch(code).Should().BeTrue($"'{code}' debería ser XXXX-XXXX-XXXX-XXXX");
    }

    [Fact]
    public void FromSource_EsDeterministico()
    {
        MachineCode.FromSource("abc").Should().Be(MachineCode.FromSource("abc"));
    }

    [Fact]
    public void FromSource_FuentesDistintas_DanCodigosDistintos()
    {
        MachineCode.FromSource("PC-recepcion").Should().NotBe(MachineCode.FromSource("PC-administracion"));
    }

    [Fact]
    public void FromSource_ValorConocido_NoCambiaEntreVersiones()
    {
        // Cambiar el algoritmo invalidaría todas las licencias ya atadas a una PC: este vector lo impide.
        MachineCode.FromSource("abc").Should().Be("86B1-0AE4-17B2-A752");
    }

    [Theory]
    [InlineData("A1B2-C3D4-E5F6-0718", "A1B2-C3D4-E5F6-0718")]
    [InlineData("a1b2-c3d4-e5f6-0718", "A1B2-C3D4-E5F6-0718")]
    [InlineData("a1b2c3d4e5f60718", "A1B2-C3D4-E5F6-0718")]
    [InlineData("  A1B2 C3D4 E5F6 0718  ", "A1B2-C3D4-E5F6-0718")]
    public void TryNormalize_AceptaMinusculasEspaciosYGuionesFaltantes(string text, string expected)
    {
        var ok = MachineCode.TryNormalize(text, out var code);

        ok.Should().BeTrue();
        code.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A1B2-C3D4-E5F6")]               // faltan dígitos
    [InlineData("A1B2-C3D4-E5F6-0718-9ABC")]     // sobran dígitos
    [InlineData("G1B2-C3D4-E5F6-0718")]          // no es hexadecimal
    public void TryNormalize_TextoQueNoEsUnCodigo_DevuelveFalse(string? text)
    {
        var ok = MachineCode.TryNormalize(text, out var code);

        ok.Should().BeFalse();
        code.Should().BeNull();
    }

    [Fact]
    public void TryNormalize_ElCodigoGeneradoSiempreEsNormalizable()
    {
        var generated = MachineCode.FromSource("cualquier-cosa");

        MachineCode.TryNormalize(generated, out var code).Should().BeTrue();
        code.Should().Be(generated);
    }
}
