using FluentAssertions;
using GymForge.Application.Services;

namespace GymForge.Application.Tests.Services;

/// <summary>Estado del resguardo externo en Configuración: texto + si hay que llamar la atención.</summary>
public class OffsiteBackupStatusTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 15, 0, 0);
    private static readonly DateTime Last = new(2026, 10, 28, 14, 3, 0);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinDestinoConfigurado_AvisaQueSoloHayCopiaLocal_SinAlarma(string? dir)
    {
        var status = OffsiteBackupStatus.Evaluate(dir, null, destinationAvailable: false, Now);

        status.Display.Should().Contain("solo en esta PC");
        status.NeedsAttention.Should().BeFalse("la copia externa es opcional");
    }

    [Fact]
    public void ConDestinoPeroSinCopiasTodavia_PideAtencion()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", null, destinationAvailable: true, Now);

        status.Display.Should().Contain("Todavía no se hizo ningún resguardo externo");
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void ConDestinoSinCopiasYNoDisponible_LoDiceTambien()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", null, destinationAvailable: false, Now);

        status.Display.Should().Contain("Todavía no").And.Contain("no está disponible");
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void CopiaReciente_MuestraLaFechaLocalSinAlarma()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", Last, destinationAvailable: true, Now);

        status.Display.Should().Be("Último resguardo externo: 28/10 14:03");
        status.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void CopiaDeHaceExactamenteSieteDias_TodaviaEsReciente()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", Now.AddDays(-7), destinationAvailable: true, Now);

        status.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void CopiaDeHaceMasDeSieteDias_EstaVencida()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", Now.AddDays(-7).AddMinutes(-1), destinationAvailable: true, Now);

        status.Display.Should().StartWith("Último resguardo externo: 21/10 14:59").And.Contain("hace más de 7 días");
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void DestinoNoDisponibleHoy_PideAtencionAunqueLaCopiaSeaReciente()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", Last, destinationAvailable: false, Now);

        status.Display.Should().StartWith("Último resguardo externo: 28/10 14:03").And.Contain("no está disponible");
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void SinFraseDeAcceso_PideAtencionAunqueLaCopiaSeaReciente()
    {
        var status = OffsiteBackupStatus.Evaluate(@"F:\", Last, destinationAvailable: true, Now, passphraseSet: false);

        status.Display.Should().Contain("frase de acceso");
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void SinDestino_LaFraseFaltanteNoImporta()
    {
        var status = OffsiteBackupStatus.Evaluate(null, null, destinationAvailable: false, Now, passphraseSet: false);

        status.NeedsAttention.Should().BeFalse();
    }
}
