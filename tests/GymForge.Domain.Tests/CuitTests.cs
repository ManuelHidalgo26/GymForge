using FluentAssertions;
using GymForge.Domain.ValueObjects;

namespace GymForge.Domain.Tests;

public class CuitTests
{
    // Dígito verificador correcto (módulo 11).
    private const string Valid = "30-71234567-1";

    [Theory]
    [InlineData("30-71234567-1")]
    [InlineData("30712345671")]
    [InlineData("  30-71234567-1  ")]
    public void TryParse_CuitValido_NormalizaAlFormatoConGuiones(string text)
    {
        var parsed = Cuit.TryParse(text, out var cuit);

        parsed.Should().BeTrue();
        cuit!.Value.Should().Be(Valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("30-71234567-8")]       // dígito verificador incorrecto
    [InlineData("30-7123456-1")]        // faltan dígitos
    [InlineData("30-71234567-12")]      // sobran dígitos
    [InlineData("30-7123456A-1")]       // letras
    public void TryParse_CuitInvalido_DevuelveFalseSinLanzar(string? text)
    {
        var parsed = Cuit.TryParse(text, out var cuit);

        parsed.Should().BeFalse();
        cuit.Should().BeNull();
    }

    [Fact]
    public void Constructor_CuitInvalido_SigueLanzando()
    {
        var create = () => new Cuit("30-71234567-8");

        create.Should().Throw<ArgumentException>().WithMessage("*verificador*");
    }
}
