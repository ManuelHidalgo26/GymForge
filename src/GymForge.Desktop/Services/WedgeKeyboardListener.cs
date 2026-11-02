using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace GymForge.Desktop.Services;

/// <summary>
/// Conecta el teclado de UNA vista con un <see cref="KeyboardWedgeDetector"/>: ve las teclas aunque otro control
/// tenga el foco (túnel, incluso las ya manejadas), traga las de una ráfaga de lector y avisa por
/// <see cref="Scanned"/> con el código completo. Es glue sin lógica de tiempos: eso vive en el detector.
/// </summary>
/// <remarks>
/// Se crea al cargar la vista y se desecha al descargarla, así solo captura mientras esa pantalla está viva
/// (kiosk, caja) y no en Configuración, Socios, etc. Se usa desde el hilo de UI.
/// <para>
/// Usar <see cref="ForWindowOf"/> y no la vista como raíz: una tecla viaja solo por los ancestros del control con
/// foco, así que un listener en la vista no oye nada si el foco quedó fuera de ella (p. ej. en el botón que abrió
/// el modal de la caja, que no es hijo del modal).
/// </para>
/// <para>
/// Ojo con los <c>KeyBinding</c> de Enter: Avalonia los procesa ANTES de rutear la tecla, así que se disparan con el
/// Enter final del lector aunque este listener lo trague. En una pantalla con listener, atender Enter con un handler
/// de túnel en el control (ver <c>CheckInKioskView</c>) y no con un <c>KeyBinding</c>.
/// </para>
/// </remarks>
public sealed class WedgeKeyboardListener : IDisposable
{
    private readonly InputElement _root;
    private readonly KeyboardWedgeDetector _detector;

    /// <summary>Un lector cerró un escaneo: el código leído, sin el Enter final.</summary>
    public event Action<string>? Scanned;

    /// <summary>Escucha el teclado de toda la ventana de <paramref name="view"/>, tenga el foco quien lo tenga.</summary>
    public static WedgeKeyboardListener ForWindowOf(InputElement view, WedgeOptions? options = null)
    {
        InputElement root = TopLevel.GetTopLevel(view) ?? view;
        return new WedgeKeyboardListener(root, options);
    }

    public WedgeKeyboardListener(InputElement root, WedgeOptions? options = null)
    {
        _root = root;
        _detector = new KeyboardWedgeDetector(options);
        _root.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    public void Dispose() => _root.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!TryMapKey(e.Key, e.KeySymbol, out var c, out var isTerminator)) return;

        var result = _detector.Push(c, isTerminator, NowMs());

        if (result.Action == WedgeAction.Swallow)
            e.Handled = true;

        if (result.CompletedScan is { } code)
            Scanned?.Invoke(code);
    }

    // KeyEventArgs no trae el instante del evento: se mide al recibirlo, con el reloj de alta resolución.
    private static long NowMs() => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    /// <summary>
    /// Traduce una tecla al carácter que el detector necesita. Enter cierra el escaneo; el resto cuenta si
    /// produce un carácter imprimible (<see cref="KeyEventArgs.KeySymbol"/>). Las teclas sin carácter (Shift, Ctrl,
    /// flechas) y las combinaciones que dan un carácter de control (Ctrl+V) se ignoran.
    /// </summary>
    public static bool TryMapKey(Key key, string? keySymbol, out char c, out bool isTerminator)
    {
        isTerminator = key == Key.Enter;
        if (isTerminator)
        {
            c = '\r';
            return true;
        }

        if (keySymbol is { Length: 1 } && !char.IsControl(keySymbol[0]))
        {
            c = keySymbol[0];
            return true;
        }

        c = default;
        return false;
    }
}
