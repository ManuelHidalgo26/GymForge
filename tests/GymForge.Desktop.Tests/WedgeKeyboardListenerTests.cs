using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using FluentAssertions;
using GymForge.Desktop.Services;

namespace GymForge.Desktop.Tests;

/// <summary>
/// La traducción tecla → carácter y el enganche a una vista (sobre un control suelto, sin ventana). La lógica de
/// tiempos está en <see cref="KeyboardWedgeDetectorTests"/>; con un lector real y la pantalla armada se verifica a mano.
/// </summary>
public class WedgeKeyboardListenerTests
{
    [Theory]
    [InlineData(Key.D7, "7", '7')]
    [InlineData(Key.NumPad4, "4", '4')]
    [InlineData(Key.A, "A", 'A')]
    [InlineData(Key.A, "a", 'a')]
    [InlineData(Key.OemMinus, "-", '-')]
    [InlineData(Key.Space, " ", ' ')]
    public void TryMapKey_AKeyThatTypesACharacter_YieldsIt(Key key, string symbol, char expected)
    {
        WedgeKeyboardListener.TryMapKey(key, symbol, out var c, out var isTerminator).Should().BeTrue();

        c.Should().Be(expected);
        isTerminator.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("7")]
    public void TryMapKey_Enter_IsTheTerminator_WhateverItsSymbol(string? symbol)
    {
        WedgeKeyboardListener.TryMapKey(Key.Enter, symbol, out _, out var isTerminator).Should().BeTrue();

        isTerminator.Should().BeTrue();
    }

    [Theory]
    [InlineData(Key.LeftShift, null)]       // modificadores: no escriben nada
    [InlineData(Key.Left, null)]            // navegación
    [InlineData(Key.F5, null)]
    [InlineData(Key.V, "\u0016")]           // Ctrl+V llega como carácter de control
    [InlineData(Key.Back, "\b")]
    [InlineData(Key.Tab, "\t")]
    [InlineData(Key.A, "")]
    [InlineData(Key.A, "ab")]               // más de un carácter: tecla muerta o secuencia, no un dígito del lector
    public void TryMapKey_AKeyWithoutAPrintableCharacter_IsIgnored(Key key, string? symbol)
    {
        WedgeKeyboardListener.TryMapKey(key, symbol, out _, out var isTerminator).Should().BeFalse();

        isTerminator.Should().BeFalse();
    }

    // ── Enganche a una vista ──────────────────────────────────────────────────

    private static KeyEventArgs KeyDown(Key key, string? symbol) => new()
    {
        RoutedEvent = InputElement.KeyDownEvent,
        Key = key,
        KeySymbol = symbol,
    };

    /// <summary>Teclea cada carácter pegado al anterior (como un lector) y devuelve los eventos para ver cuáles se tragaron.</summary>
    private static List<KeyEventArgs> Burst(Control view, string text, bool enter)
    {
        var events = text.Select(c => KeyDown(Key.D0, c.ToString())).ToList();
        if (enter) events.Add(KeyDown(Key.Enter, null));
        foreach (var e in events) view.RaiseEvent(e);
        return events;
    }

    [AvaloniaFact]
    public void Listener_ABurstWithEnter_RaisesScannedWithTheCode_AndSwallowsAllButTheFirstKey()
    {
        var view = new Border();
        using var listener = new WedgeKeyboardListener(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        var events = Burst(view, "7791234567890", enter: true);

        scans.Should().Equal("7791234567890");
        events[0].Handled.Should().BeFalse("la primera tecla de la ráfaga pasa");
        events.Skip(1).Should().OnlyContain(e => e.Handled, "el resto, y el Enter, se tragan");
    }

    [AvaloniaFact]
    public void Listener_SlowTyping_IsNeverSwallowedNorScanned()
    {
        var view = new Border();
        using var listener = new WedgeKeyboardListener(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        var events = new List<KeyEventArgs>();
        foreach (var symbol in new[] { "3", "0", "1", "1" })
        {
            var e = KeyDown(Key.D0, symbol);
            view.RaiseEvent(e);
            events.Add(e);
            Thread.Sleep(80);   // una persona: bien por encima de los 50 ms entre teclas de un lector
        }
        var enter = KeyDown(Key.Enter, null);
        view.RaiseEvent(enter);

        scans.Should().BeEmpty();
        events.Should().OnlyContain(e => !e.Handled);
        enter.Handled.Should().BeFalse("el Enter manual lo maneja el control de siempre");
    }

    [AvaloniaFact]
    public void Listener_KeysWithoutACharacter_AreIgnored()
    {
        var view = new Border();
        using var listener = new WedgeKeyboardListener(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        var shift = KeyDown(Key.LeftShift, null);
        view.RaiseEvent(shift);
        Burst(view, "AB", enter: false);
        var ctrlV = KeyDown(Key.V, "\u0016");
        view.RaiseEvent(ctrlV);
        view.RaiseEvent(KeyDown(Key.Enter, null));

        shift.Handled.Should().BeFalse();
        ctrlV.Handled.Should().BeFalse();
        scans.Should().BeEmpty("solo hay 2 caracteres: menos del mínimo");
    }

    [AvaloniaFact]
    public void Listener_AfterDispose_StopsListening()
    {
        var view = new Border();
        var listener = new WedgeKeyboardListener(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        listener.Dispose();
        var events = Burst(view, "7791234567890", enter: true);

        scans.Should().BeEmpty();
        events.Should().OnlyContain(e => !e.Handled);
    }

    [AvaloniaFact]
    public void ForWindowOf_AViewNotInsideAWindow_ListensOnTheViewItself()
    {
        // Sin ventana no hay TopLevel (en un test no hay plataforma de render): cae a la propia vista. Con ventana
        // escucha toda la ventana; eso se verifica a mano y en tools/GymForge.Screenshots (escaneo del POS con teclas
        // reales y el foco en el botón que abrió el modal).
        var view = new Border();
        using var listener = WedgeKeyboardListener.ForWindowOf(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        Burst(view, "7791234567890", enter: true);

        scans.Should().Equal("7791234567890");
    }

    [AvaloniaFact]
    public void Listener_SeesKeysEvenWhenAnotherHandlerAlreadyHandledThem()
    {
        var view = new Border();
        view.AddHandler(InputElement.KeyDownEvent, (_, e) => e.Handled = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        using var listener = new WedgeKeyboardListener(view);
        var scans = new List<string>();
        listener.Scanned += scans.Add;

        Burst(view, "ABC123", enter: true);

        scans.Should().Equal("ABC123");
    }
}
