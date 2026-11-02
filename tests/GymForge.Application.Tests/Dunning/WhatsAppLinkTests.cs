using FluentAssertions;
using GymForge.Application.UseCases.Dunning;

namespace GymForge.Application.Tests.Dunning;

public class WhatsAppLinkTests
{
    // ── Celulares argentinos en los formatos en que se cargan a mano ─────────

    [Theory]
    [InlineData("11 1234-5678", "5491112345678")]            // área + número
    [InlineData("011 15 2345-6789", "5491123456789")]        // 0 + área + 15 + número (legacy)
    [InlineData("11 15 2345-6789", "5491123456789")]         // sin el 0, con 15
    [InlineData("0351 15 412-3456", "5493514123456")]        // área de 3 dígitos
    [InlineData("02966 15 123456", "5492966123456")]         // área de 4 dígitos
    [InlineData("+54 9 11 2345 6789", "5491123456789")]      // ya internacional con el 9
    [InlineData("+54 11 2345 6789", "5491123456789")]        // internacional sin el 9
    [InlineData("5491123456789", "5491123456789")]           // solo dígitos, ya internacional
    [InlineData("9 11 2345 6789", "5491123456789")]          // el 9 sin el código de país
    [InlineData("+54 9 11 15 2345 6789", "5491123456789")]   // internacional con 15 de sobra
    [InlineData("(011) 2345-6789", "5491123456789")]         // paréntesis y guiones
    public void ToWhatsAppDigits_NormalizaCelularesArgentinos(string raw, string expected)
    {
        WhatsAppLink.ToWhatsAppDigits(raw).Should().Be(expected);
    }

    [Fact]
    public void ToWhatsAppDigits_SinCodigoDeArea_QuitaElQuinceYDejaLoQueQueda()
    {
        // Límite documentado: sin código de área no hay forma de armar un número completo (best-effort).
        WhatsAppLink.ToWhatsAppDigits("1512345678").Should().Be("54912345678");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin celular")]
    [InlineData("---")]
    [InlineData("0")]
    [InlineData("15")]
    [InlineData("11 5555-0000 / 11 4444-1111")]              // dos números en el mismo campo: pasa los 15 dígitos de E.164
    [InlineData("9999999999999999999999999999999999")]
    public void ToWhatsAppDigits_SinDigitosUsables_DevuelveNull(string? raw)
    {
        WhatsAppLink.ToWhatsAppDigits(raw).Should().BeNull();
    }

    // ── Link ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_ArmaElLinkConElTextoUrlEncoded()
    {
        var url = WhatsAppLink.Build("11 2345-6789", "¡Hola Ana! Debés $35.000 & más");

        url.Should().Be("https://wa.me/5491123456789?text=%C2%A1Hola%20Ana%21%20Deb%C3%A9s%20%2435.000%20%26%20m%C3%A1s");
    }

    [Fact]
    public void Build_CodificaLosSaltosDeLinea()
    {
        WhatsAppLink.Build("11 2345-6789", "uno\ndos").Should().EndWith("?text=uno%0Ados");
    }

    [Fact]
    public void Build_ConLasPlantillasDeFabricaYDatosLargos_QuedaBajoElLimiteDeUrlDelShell()
    {
        // ShellExecute falla con URLs de más de ~2048 caracteres: las plantillas de fábrica tienen que entrar con holgura.
        var context = new DunningContext(
            "Maximiliano Nicolás", "Gimnasio Nuevo Horizonte Sociedad de Responsabilidad Limitada",
            1_234_567m, new DateOnly(2026, 9, 17), 365);

        foreach (var stage in DunningConfig.DefaultStages)
        {
            var url = WhatsAppLink.Build("011 15 2345-6789", DunningMessageRenderer.Render(stage.Template, context));

            url.Length.Should().BeLessThan(1000, $"la etapa de {stage.DaysOverdue} días no debería acercarse al límite");
        }
    }

    [Fact]
    public void Build_ConUnTelefonoSinDigitos_Lanza()
    {
        var act = () => WhatsAppLink.Build("sin celular", "Hola");

        act.Should().Throw<ArgumentException>();
    }
}
