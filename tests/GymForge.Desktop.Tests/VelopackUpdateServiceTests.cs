using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Velopack;

namespace GymForge.Desktop.Tests;

/// <summary>
/// El UpdateManager real necesita la app instalada y un feed publicado, así que acá solo se prueba lo que
/// pasa antes de tocar la red: feed vacío, copia sin instalar (dev/portable) y uso fuera de secuencia.
/// Ningún test hace requests: el feed de ejemplo es un dominio reservado (.invalid).
/// </summary>
public class VelopackUpdateServiceTests
{
    private readonly ILocalSettings _settings = Substitute.For<ILocalSettings>();

    private VelopackUpdateService Sut() => new(_settings, NullLogger<VelopackUpdateService>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Check_SinFeedConfigurado_EsNoOp(string? feedUrl)
    {
        _settings.UpdateFeedUrl.Returns(feedUrl);

        var result = await Sut().CheckAsync();

        result.Should().Be(UpdateCheckResult.FeedNotConfigured);
    }

    [Theory]
    [InlineData("https://github.com/acme/gymforge")]
    [InlineData("https://updates.gymforge.invalid/releases")]
    [InlineData(@"\\servidor\actualizaciones\gymforge")]
    public async Task Check_FeedConfiguradoPeroAppSinInstalar_NoConsultaLaRed(string feedUrl)
    {
        // El host de tests no corre instalado por Velopack, igual que un `dotnet run` o el exe portable.
        BootVelopackLikeProgramDoes();
        _settings.UpdateFeedUrl.Returns(feedUrl);

        var result = await Sut().CheckAsync();

        result.Should().Be(UpdateCheckResult.NotInstalled);
    }

    [Fact]
    public async Task Check_LeeElFeedEnCadaConsulta_ParaTomarCambiosDeConfiguracion()
    {
        BootVelopackLikeProgramDoes();
        var sut = Sut();
        _settings.UpdateFeedUrl.Returns((string?)null);
        (await sut.CheckAsync()).Should().Be(UpdateCheckResult.FeedNotConfigured);

        _settings.UpdateFeedUrl.Returns("https://updates.gymforge.invalid/releases");

        (await sut.CheckAsync()).Should().Be(UpdateCheckResult.NotInstalled);
    }

    /// <summary>Lo mismo que hace Program.cs al arrancar; deja el estado de Velopack igual que en un `dotnet run`.</summary>
    private static void BootVelopackLikeProgramDoes() => VelopackApp.Build().Run();

    [Fact]
    public async Task Download_SinHaberBuscadoAntes_DevuelveFalseSinLanzar()
    {
        var download = () => Sut().DownloadAsync(new AvailableUpdate("0.4.0"));

        (await download.Should().NotThrowAsync()).Which.Should().BeFalse();
    }

    [Fact]
    public void Apply_SinActualizacionDescargada_LanzaConMensajeClaro()
    {
        var sut = Sut();
        var update = new AvailableUpdate("0.4.0");

        sut.Invoking(s => s.ApplyAndRestart(update)).Should().Throw<InvalidOperationException>()
            .WithMessage("*actualización descargada*");
        sut.Invoking(s => s.ApplyOnExit(update)).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void CurrentVersion_EsLaDelEnsamblado_SinMetadataDeBuild()
    {
        var version = Sut().CurrentVersion;

        version.Should().MatchRegex(@"^\d+\.\d+\.\d+");
        version.Should().NotContain("+");
    }
}
