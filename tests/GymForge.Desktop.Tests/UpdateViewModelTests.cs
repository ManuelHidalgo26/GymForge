using FluentAssertions;
using GymForge.Application.Interfaces;
using GymForge.Desktop.ViewModels.Shared;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace GymForge.Desktop.Tests;

/// <summary>
/// Aviso de "actualización lista": busca al arrancar, descarga en silencio y deja que el usuario elija cuándo
/// reiniciar. Nada de esto puede romper la app de recepción si el feed no existe o no hay red.
/// </summary>
public class UpdateViewModelTests
{
    private static readonly AvailableUpdate NewVersion = new("0.4.0");

    private readonly IUpdateService _updates = Substitute.For<IUpdateService>();

    public UpdateViewModelTests()
    {
        _updates.CurrentVersion.Returns("0.3.0");
        _updates.DownloadAsync(Arg.Any<AvailableUpdate>()).Returns(true);
    }

    private UpdateViewModel Sut() => new(_updates);

    private void FeedReturns(UpdateCheckResult result) => _updates.CheckAsync().Returns(result);

    private async Task<UpdateViewModel> SutWithUpdateReadyAsync()
    {
        FeedReturns(UpdateCheckResult.Found(NewVersion));
        var vm = Sut();
        await vm.CheckAsync();
        return vm;
    }

    // ── Hay actualización ────────────────────────────────────────────────────

    [Fact]
    public async Task Check_HayActualizacion_LaDescargaYMuestraElBanner()
    {
        FeedReturns(UpdateCheckResult.Found(NewVersion));
        var vm = Sut();

        await vm.CheckAsync();

        await _updates.Received(1).DownloadAsync(NewVersion);
        vm.IsUpdateReady.Should().BeTrue();
        vm.UpdateVersion.Should().Be("0.4.0");
        vm.IsBannerVisible.Should().BeTrue();
        vm.StatusMessage.Should().Contain("0.4.0");
    }

    [Fact]
    public async Task Check_HayActualizacionPeroFallaLaDescarga_NoMuestraElBannerYAvisaEnConfiguracion()
    {
        FeedReturns(UpdateCheckResult.Found(NewVersion));
        _updates.DownloadAsync(NewVersion).Returns(false);
        var vm = Sut();

        await vm.CheckAsync();

        vm.IsUpdateReady.Should().BeFalse();
        vm.IsBannerVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("no se pudo descargar");
    }

    [Fact]
    public async Task Check_LaDescargaLanza_NoMuestraElBannerNiPropaga()
    {
        FeedReturns(UpdateCheckResult.Found(NewVersion));
        _updates.DownloadAsync(NewVersion).ThrowsAsync(new IOException("disco lleno"));
        var vm = Sut();

        var check = vm.CheckAsync;

        await check.Should().NotThrowAsync();
        vm.IsBannerVisible.Should().BeFalse();
        vm.IsChecking.Should().BeFalse();
    }

    // ── No hay actualización / auto-update inactivo ──────────────────────────

    [Fact]
    public async Task Check_NoHayActualizacion_BannerOcultoYSeDiceQueEstaAlDia()
    {
        FeedReturns(UpdateCheckResult.UpToDate);
        var vm = Sut();

        await vm.CheckAsync();

        vm.IsBannerVisible.Should().BeFalse();
        vm.IsUpdateReady.Should().BeFalse();
        vm.StatusMessage.Should().Contain("última versión");
        await _updates.DidNotReceive().DownloadAsync(Arg.Any<AvailableUpdate>());
    }

    [Fact]
    public async Task Check_FeedSinConfigurar_BannerOcultoYSeExplicaPorQue()
    {
        FeedReturns(UpdateCheckResult.FeedNotConfigured);
        var vm = Sut();

        await vm.CheckAsync();

        vm.IsBannerVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("origen de actualizaciones");
        await _updates.DidNotReceive().DownloadAsync(Arg.Any<AvailableUpdate>());
    }

    [Fact]
    public async Task Check_AppSinInstalar_BannerOcultoYSeExplicaPorQue()
    {
        FeedReturns(UpdateCheckResult.NotInstalled);
        var vm = Sut();

        await vm.CheckAsync();

        vm.IsBannerVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("instalador");
    }

    // ── El feed falla: no rompe ──────────────────────────────────────────────

    [Fact]
    public async Task Check_FalloElFeed_BannerOcultoYSigueOperando()
    {
        FeedReturns(UpdateCheckResult.Failed);
        var vm = Sut();

        await vm.CheckAsync();

        vm.IsBannerVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("No se pudo buscar");
        vm.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task Check_ElServiceLanza_BannerOcultoSinPropagar()
    {
        _updates.CheckAsync().ThrowsAsync(new InvalidOperationException("el feed devolvió basura"));
        var vm = Sut();

        var check = vm.CheckAsync;

        await check.Should().NotThrowAsync();
        vm.IsBannerVisible.Should().BeFalse();
        vm.StatusMessage.Should().Contain("No se pudo buscar");
        vm.IsChecking.Should().BeFalse();
    }

    // ── Reentrancia ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Check_MientrasYaEstaBuscando_NoDuplicaLaConsulta()
    {
        var feed = new TaskCompletionSource<UpdateCheckResult>();
        _updates.CheckAsync().Returns(feed.Task);
        var vm = Sut();

        var first = vm.CheckAsync();
        vm.IsChecking.Should().BeTrue();
        await vm.CheckAsync();
        feed.SetResult(UpdateCheckResult.UpToDate);
        await first;

        await _updates.Received(1).CheckAsync();
        vm.IsChecking.Should().BeFalse();
    }

    [Fact]
    public async Task Check_YaHayUnaActualizacionLista_NoVuelveAConsultarElFeed()
    {
        var vm = await SutWithUpdateReadyAsync();
        _updates.ClearReceivedCalls();

        await vm.CheckAsync();

        await _updates.DidNotReceive().CheckAsync();
        vm.IsBannerVisible.Should().BeTrue();
    }

    // ── "Reiniciar ahora" / "Después" ────────────────────────────────────────

    [Fact]
    public async Task RestartNow_AplicaLaActualizacionYReinicia()
    {
        var vm = await SutWithUpdateReadyAsync();

        vm.RestartNowCommand.Execute(null);

        _updates.Received(1).ApplyAndRestart(NewVersion);
    }

    [Fact]
    public void RestartNow_SinActualizacionLista_NoHaceNada()
    {
        var vm = Sut();

        vm.RestartNowCommand.Execute(null);

        _updates.DidNotReceive().ApplyAndRestart(Arg.Any<AvailableUpdate>());
    }

    [Fact]
    public async Task RestartNow_SiNoSePudoAplicar_ConservaElBannerYAvisa()
    {
        var vm = await SutWithUpdateReadyAsync();
        _updates.When(u => u.ApplyAndRestart(NewVersion)).Throw(new IOException("no se pudo lanzar Update.exe"));

        var restart = () => vm.RestartNowCommand.Execute(null);

        restart.Should().NotThrow();
        vm.IsBannerVisible.Should().BeTrue();
        vm.StatusMessage.Should().Contain("No se pudo iniciar la actualización");
    }

    [Fact]
    public async Task Later_OcultaElBannerYProgramaLaInstalacionAlCerrar()
    {
        var vm = await SutWithUpdateReadyAsync();

        vm.LaterCommand.Execute(null);

        _updates.Received(1).ApplyOnExit(NewVersion);
        _updates.DidNotReceive().ApplyAndRestart(Arg.Any<AvailableUpdate>());
        vm.IsBannerVisible.Should().BeFalse();
        vm.IsUpdateReady.Should().BeTrue();   // sigue descargada y con la instalación programada
        vm.StatusMessage.Should().Contain("al cerrar");
    }

    [Fact]
    public async Task Later_SiNoSePudoProgramar_IgualOcultaElBannerYNoPropaga()
    {
        var vm = await SutWithUpdateReadyAsync();
        _updates.When(u => u.ApplyOnExit(NewVersion)).Throw(new IOException("no se pudo lanzar Update.exe"));

        var later = () => vm.LaterCommand.Execute(null);

        later.Should().NotThrow();
        vm.IsBannerVisible.Should().BeFalse();
    }

    // ── Versión actual ───────────────────────────────────────────────────────

    [Fact]
    public void CurrentVersion_ViajaDesdeElService()
    {
        Sut().CurrentVersion.Should().Be("0.3.0");
    }

    [Fact]
    public void AntesDeBuscar_NoHayBannerNiMensaje()
    {
        var vm = Sut();

        vm.IsBannerVisible.Should().BeFalse();
        vm.IsUpdateReady.Should().BeFalse();
        vm.StatusMessage.Should().BeEmpty();
    }
}
