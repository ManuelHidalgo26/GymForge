using FluentAssertions;
using GymForge.Application.UseCases.Dunning;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.Tests;

/// <summary>
/// <c>Open</c> lanza un proceso del SO: solo se prueban sus rechazos (jamás llegan a <c>Process.Start</c>, que es
/// justo lo que se verifica). La decisión de qué se abre vive en <c>IsWhatsAppLink</c>, que sí se prueba entera.
/// </summary>
public class WhatsAppLinkSenderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("no es una url")]
    [InlineData(@"C:\Windows\System32\calc.exe")]
    [InlineData("calc.exe")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("http://wa.me/5491123456789?text=hola")]           // sin https
    [InlineData("https://wa.me.evil.com/5491123456789")]           // host distinto
    [InlineData("https://evil.com/wa.me/5491123456789")]
    [InlineData("whatsapp://send?phone=5491123456789")]
    public void Open_RechazaCualquierCosaQueNoSeaUnLinkHttpsDeWaMe(string url)
    {
        new WhatsAppLinkSender().Open(url).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://wa.me/5491123456789")]
    [InlineData("https://wa.me/5491123456789?text=")]
    [InlineData("https://wa.me/5491123456789?text=Hola%20Ana%21%20%C2%A1Gracias%21")]
    [InlineData("https://wa.me/5491123456789?text=a-b_c.d~e")]
    public void IsWhatsAppLink_AceptaElFormatoQueArmaWhatsAppLink(string url)
    {
        WhatsAppLinkSender.IsWhatsAppLink(url).Should().BeTrue();
    }

    [Theory]
    // Esquema y host
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://wa.me/5491123456789")]
    [InlineData("HTTPS://WA.ME/5491123456789")]
    [InlineData("https://wa.me.evil.com/5491123456789")]
    [InlineData("https://evil.com/?x=wa.me")]
    [InlineData("https://evil.com#wa.me")]
    [InlineData("https://wa.me@evil.com/5491123456789")]
    [InlineData("https://evil.com@wa.me/5491123456789")]            // user@host: el host real es wa.me, pero no es nuestro formato
    [InlineData("https://wa.me:8080/5491123456789")]
    [InlineData("https://wa.me./5491123456789")]
    [InlineData(@"https://wa.me\@evil.com/5491123456789")]
    [InlineData("//wa.me/5491123456789")]
    [InlineData(@"\\wa.me\share\x.exe")]
    [InlineData("file://wa.me/c$/windows/system32/calc.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ms-msdt:/id PCWDiagnostic")]
    // Lo que Uri.TryCreate toleraba y ShellExecute recibiría tal cual: comillas, espacios, control, bordes
    [InlineData("https://wa.me/5491123456789?text=hola\" --flag")]
    [InlineData("https://wa.me/5491123456789?text=hola mundo")]
    [InlineData("https://wa.me/5491123456789?text=hola\nmundo")]
    [InlineData("https://wa.me/5491123456789?text=hola\r\nmundo")]
    [InlineData("https://wa.me/5491123456789?text=hola\tmundo")]
    [InlineData(" https://wa.me/5491123456789")]
    [InlineData("https://wa.me/5491123456789 ")]
    [InlineData("https://wa.me/5491123456789\n")]
    // Otras rutas, parámetros o fragmentos de wa.me
    [InlineData("https://wa.me/")]
    [InlineData("https://wa.me")]
    [InlineData("https://wa.me/message/ABCDEF")]
    [InlineData("https://wa.me/5491123456789/otra/ruta")]
    [InlineData("https://wa.me/5491123456789?text=hola&otro=1")]
    [InlineData("https://wa.me/5491123456789?phone=1")]
    [InlineData("https://wa.me/5491123456789?text=hola#frag")]
    [InlineData("https://wa.me/5491123456789?text=100%")]            // % sin dos hex
    [InlineData("https://wa.me/5491123456789?text='x'")]             // ni siquiera los sub-delimitadores
    [InlineData("https://wa.me/+5491123456789")]
    [InlineData("https://wa.me/549112345678901234567")]              // más de 20 dígitos
    public void IsWhatsAppLink_RechazaTodoLoDemas(string? url)
    {
        WhatsAppLinkSender.IsWhatsAppLink(url).Should().BeFalse();
    }

    [Fact]
    public void IsWhatsAppLink_RechazaUnCaracterNulo()
    {
        WhatsAppLinkSender.IsWhatsAppLink("https://wa.me/5491123456789?text=hola\0mundo").Should().BeFalse();
    }

    [Theory]
    [InlineData("+54 9 11 2345-6789")]
    [InlineData("011 15 2345-6789")]
    [InlineData("2234 15-123456")]
    [InlineData("1234")]
    public void LoQueArmaWhatsAppLink_SiempreLoAceptaElSender_AunConMensajesHostiles(string phone)
    {
        string[] hostileMessages =
        [
            "¡Hola Ñandú! Tu cuota de \"Gym & Co\" venció (saldo $35.000,00).",
            "Juan'; calc.exe & echo \"x\" | more ^ < > `",
            "linea 1\r\nlinea 2\tcon tab\0nul",
            "100% seguro %41 %zz {nombre} {x}",
            "😀 emoji \uD800 sustituto suelto",
            "https://evil.com/?x=wa.me --flag \" -new-tab",
            new string('ñ', 300),
            string.Empty,
        ];

        foreach (var message in hostileMessages)
        {
            var url = WhatsAppLink.Build(phone, message);

            WhatsAppLinkSender.IsWhatsAppLink(url).Should().BeTrue($"el link armado para «{message}» debe pasar la allowlist: {url}");
        }
    }
}
