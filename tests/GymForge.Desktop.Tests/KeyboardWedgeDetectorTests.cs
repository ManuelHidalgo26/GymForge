using FluentAssertions;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.Tests;

public class KeyboardWedgeDetectorTests
{
    private const string Code = "7791234567890";

    /// <summary>Teclado simulado: cada tecla avanza el reloj <c>gapMs</c> desde la anterior.</summary>
    private sealed class Keyboard(WedgeOptions? options = null)
    {
        private readonly KeyboardWedgeDetector _detector = new(options);
        private long _nowMs;

        public WedgeResult Key(char c, int gapMs)
        {
            _nowMs += gapMs;
            return _detector.Push(c, isTerminator: false, _nowMs);
        }

        public WedgeResult Enter(int gapMs)
        {
            _nowMs += gapMs;
            return _detector.Push('\r', isTerminator: true, _nowMs);
        }

        public List<WedgeResult> Type(string text, int gapMs) => text.Select(c => Key(c, gapMs)).ToList();

        public void Wait(int ms) => _nowMs += ms;

        public void Reset() => _detector.Reset();
    }

    // ── Ráfaga de scanner ─────────────────────────────────────────────────────

    [Fact]
    public void RafagaRapidaConEnter_EmiteElCodigoYTragaElTerminador()
    {
        var keyboard = new Keyboard();
        keyboard.Type(Code, gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        enter.Action.Should().Be(WedgeAction.Swallow);
        enter.CompletedScan.Should().Be(Code);
    }

    [Fact]
    public void RafagaRapida_TragaTodasLasTeclasSalvoLaPrimeraYNoEmiteAntesDelTerminador()
    {
        var keys = new Keyboard().Type(Code, gapMs: 5);

        keys[0].Action.Should().Be(WedgeAction.Passthrough, "sin tecla previa no hay gap que medir");
        keys.Skip(1).Should().OnlyContain(r => r.Action == WedgeAction.Swallow);
        keys.Should().OnlyContain(r => r.CompletedScan == null);
    }

    [Fact]
    public void PrimerCaracterDeLaSiguienteRafaga_EsPassthroughAunSiLlegaPegadoAlEscaneoAnterior()
    {
        var keyboard = new Keyboard();
        keyboard.Type(Code, gapMs: 5);
        keyboard.Enter(gapMs: 5);

        var nextBurst = keyboard.Type(Code, gapMs: 5);

        nextBurst[0].Action.Should().Be(WedgeAction.Passthrough);
        nextBurst.Skip(1).Should().OnlyContain(r => r.Action == WedgeAction.Swallow);
    }

    [Fact]
    public void DosEscaneosSeguidos_EmitenCadaUnoConSuCodigo()
    {
        var keyboard = new Keyboard();
        keyboard.Type("AAA111", gapMs: 5);
        var first = keyboard.Enter(gapMs: 5);
        keyboard.Type("BBB222", gapMs: 5);

        var second = keyboard.Enter(gapMs: 5);

        first.CompletedScan.Should().Be("AAA111");
        second.CompletedScan.Should().Be("BBB222");
    }

    [Fact]
    public void EnterDespuesDeUnEscaneo_NoRepiteElCodigo()
    {
        var keyboard = new Keyboard();
        keyboard.Type(Code, gapMs: 5);
        keyboard.Enter(gapMs: 5);

        var secondEnter = keyboard.Enter(gapMs: 5);

        secondEnter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    // ── Tecleo manual: nunca se traga ─────────────────────────────────────────

    [Fact]
    public void TecleoLento_NoTragaNadaYNoEmite()
    {
        var keyboard = new Keyboard();
        var keys = keyboard.Type("30123456", gapMs: 150);

        var enter = keyboard.Enter(gapMs: 150);

        keys.Should().OnlyContain(r => r.Action == WedgeAction.Passthrough && r.CompletedScan == null);
        enter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    [Fact]
    public void TecleoLentoSeguidoDeEnterRapido_NoEmite()
    {
        var keyboard = new Keyboard();
        keyboard.Type("30123456", gapMs: 150);

        var enter = keyboard.Enter(gapMs: 10);

        enter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    [Fact]
    public void EnterSinNadaAcumulado_EsPassthrough()
    {
        new Keyboard().Enter(gapMs: 5).Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    // ── Ráfaga sin terminador / muy corta ─────────────────────────────────────

    [Fact]
    public void RafagaSinTerminador_NoEmiteNiSeArrastraALaSiguiente()
    {
        var keyboard = new Keyboard();
        var orphanBurst = keyboard.Type("111", gapMs: 5);
        keyboard.Wait(1000);   // pasa el timeout: la ráfaga huérfana se descarta
        keyboard.Type("222", gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        orphanBurst.Should().OnlyContain(r => r.CompletedScan == null);
        enter.CompletedScan.Should().Be("222");
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void LargoDeLaRafaga_RespetaElMinimo(int length, bool emits)
    {
        var keyboard = new Keyboard();
        keyboard.Type(new string('7', length), gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        (enter.CompletedScan is not null).Should().Be(emits);
        enter.Action.Should().Be(emits ? WedgeAction.Swallow : WedgeAction.Passthrough);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void LargoDeLaRafaga_RespetaElMaximo(int length, bool emits)
    {
        var keyboard = new Keyboard();
        keyboard.Type(new string('7', length), gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        (enter.CompletedScan is not null).Should().Be(emits);
    }

    [Fact]
    public void RafagaMasLargaQueElMaximo_DejaDeTragarYNoEmite()
    {
        var keyboard = new Keyboard();

        var keys = keyboard.Type(new string('7', 70), gapMs: 5);
        var enter = keyboard.Enter(gapMs: 5);

        keys.Count(r => r.Action == WedgeAction.Swallow).Should().Be(63, "la 1ª pasa y se traga hasta completar 64");
        keys.Skip(64).Should().OnlyContain(r => r.Action == WedgeAction.Passthrough);
        enter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    // ── Bordes de los umbrales ────────────────────────────────────────────────

    [Theory]
    [InlineData(50, WedgeAction.Swallow)]
    [InlineData(51, WedgeAction.Passthrough)]
    public void GapEntreTeclas_EnElBordeDeInterKeyScanMaxMs(int gapMs, WedgeAction expected)
    {
        var keyboard = new Keyboard();
        keyboard.Key('A', gapMs: 5);

        keyboard.Key('B', gapMs).Action.Should().Be(expected);
    }

    [Fact]
    public void GapLentoEnElMedioDeLaRafaga_ReiniciaElBufferConLaTeclaActual()
    {
        var keyboard = new Keyboard();
        keyboard.Type("111", gapMs: 5);
        keyboard.Key('2', gapMs: 100);   // entre InterKeyScanMaxMs (50) e IdleResetMs (300): tecleo manual
        keyboard.Type("222", gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        enter.CompletedScan.Should().Be("2222");
    }

    [Fact]
    public void PausaMayorAIdleResetMs_DescartaLaRafagaAnterior()
    {
        var keyboard = new Keyboard();
        keyboard.Type("111", gapMs: 5);
        keyboard.Key('2', gapMs: 301);
        keyboard.Type("222", gapMs: 5);

        var enter = keyboard.Enter(gapMs: 5);

        enter.CompletedScan.Should().Be("2222");
    }

    [Theory]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void GapHastaElTerminador_EnElBordeDeIdleResetMs(int gapMs, bool emits)
    {
        var keyboard = new Keyboard();
        keyboard.Type("1234", gapMs: 5);

        var enter = keyboard.Enter(gapMs);

        (enter.CompletedScan is not null).Should().Be(emits);
    }

    [Fact]
    public void TimestampQueRetrocede_ReiniciaLaSecuencia()
    {
        var detector = new KeyboardWedgeDetector();
        detector.Push('A', isTerminator: false, timestampMs: 1000);
        detector.Push('B', isTerminator: false, timestampMs: 1005);
        detector.Push('C', isTerminator: false, timestampMs: 1010);

        var afterGoingBack = detector.Push('D', isTerminator: false, timestampMs: 500);
        var enter = detector.Push('\r', isTerminator: true, timestampMs: 505);

        afterGoingBack.Action.Should().Be(WedgeAction.Passthrough);
        enter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    // ── Reset ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Reset_DescartaLoAcumulado()
    {
        var keyboard = new Keyboard();
        keyboard.Type("ABC", gapMs: 5);
        keyboard.Reset();

        var enter = keyboard.Enter(gapMs: 5);

        enter.Should().Be(new WedgeResult(WedgeAction.Passthrough, null));
    }

    // ── Opciones ──────────────────────────────────────────────────────────────

    [Fact]
    public void OpcionesPorDefecto_SonLasDelAdr()
    {
        var options = new WedgeOptions();

        options.InterKeyScanMaxMs.Should().Be(50);
        options.IdleResetMs.Should().Be(300);
        options.MinScanLength.Should().Be(3);
        options.MaxScanLength.Should().Be(64);
    }

    [Fact]
    public void OpcionesPersonalizadas_CambianLosUmbrales()
    {
        var keyboard = new Keyboard(new WedgeOptions(InterKeyScanMaxMs: 20, IdleResetMs: 100, MinScanLength: 5));
        keyboard.Type("1234", gapMs: 10);
        var tooShort = keyboard.Enter(gapMs: 10);
        keyboard.Type("12345", gapMs: 10);
        var longEnough = keyboard.Enter(gapMs: 10);
        var slowForCustomGap = keyboard.Type("AB", gapMs: 30);

        tooShort.CompletedScan.Should().BeNull();
        longEnough.CompletedScan.Should().Be("12345");
        slowForCustomGap.Should().OnlyContain(r => r.Action == WedgeAction.Passthrough);
    }

    [Theory]
    [InlineData(0, 300, 3, 64)]    // InterKeyScanMaxMs no positivo
    [InlineData(50, 49, 3, 64)]    // IdleResetMs menor que InterKeyScanMaxMs
    [InlineData(50, 300, 0, 64)]   // MinScanLength menor a 1
    [InlineData(50, 300, 5, 4)]    // MaxScanLength menor que MinScanLength
    public void OpcionesIncoherentes_SeRechazanAlConstruir(int interKey, int idleReset, int min, int max)
    {
        var act = () => new KeyboardWedgeDetector(new WedgeOptions(interKey, idleReset, min, max));

        act.Should().Throw<ArgumentException>();
    }
}
