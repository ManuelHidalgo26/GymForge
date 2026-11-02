using System.Text;

namespace GymForge.Desktop.Services;

/// <summary>
/// Umbrales para distinguir un lector keyboard-wedge del tecleo manual. Un wedge entrega cada carácter con
/// 1-15 ms de separación; una persona teclea con 80-300 ms entre teclas.
/// </summary>
/// <param name="InterKeyScanMaxMs">Gap máximo entre teclas DENTRO de una ráfaga de scanner.</param>
/// <param name="IdleResetMs">Gap que descarta lo acumulado: arranca una secuencia nueva. También es el
/// retraso máximo tolerado entre el último carácter y el terminador.</param>
/// <param name="MinScanLength">Mínimo de caracteres para considerar la ráfaga un escaneo.</param>
/// <param name="MaxScanLength">Tope de sanidad: una ráfaga más larga no es un código, no se acumula.</param>
public sealed record WedgeOptions(
    int InterKeyScanMaxMs = 50,
    int IdleResetMs = 300,
    int MinScanLength = 3,
    int MaxScanLength = 64);

/// <summary>Qué hacer con la tecla recién recibida.</summary>
public enum WedgeAction
{
    /// <summary>Dejar que la tecla llegue al control enfocado como tecleo normal.</summary>
    Passthrough,

    /// <summary>Tragarla: es parte de una ráfaga de scanner y no debe caer en el control enfocado.</summary>
    Swallow
}

/// <param name="Action">Qué hacer con la tecla.</param>
/// <param name="CompletedScan">El código acumulado (sin terminador) si esta tecla cerró un escaneo válido.</param>
public readonly record struct WedgeResult(WedgeAction Action, string? CompletedScan);

/// <summary>
/// Máquina de estados pura que separa una ráfaga de lector keyboard-wedge (varios caracteres muy rápidos
/// más un terminador) del tecleo manual. No conoce Avalonia ni un reloj: el llamador pasa el tiempo de cada
/// tecla, así que es determinística y testeable.
/// </summary>
/// <remarks>
/// El primer carácter de una ráfaga siempre es <see cref="WedgeAction.Passthrough"/>: sin tecla previa no hay
/// gap que medir. Ese único carácter puede llegar al control enfocado; el tecleo manual nunca se traga porque
/// sus gaps superan <see cref="WedgeOptions.InterKeyScanMaxMs"/>. No es thread-safe: se alimenta desde el hilo de UI.
/// </remarks>
public sealed class KeyboardWedgeDetector
{
    private const long NoPreviousKey = long.MaxValue;

    private static readonly WedgeResult Pass = new(WedgeAction.Passthrough, null);
    private static readonly WedgeResult Swallowed = new(WedgeAction.Swallow, null);

    private readonly WedgeOptions _options;
    private readonly StringBuilder _buffer = new();
    private long? _lastTimestampMs;

    public KeyboardWedgeDetector(WedgeOptions? options = null)
    {
        _options = options ?? new WedgeOptions();
        Validate(_options);
    }

    /// <summary>
    /// Alimenta una tecla. Devuelve si hay que tragarla y, si un terminador cerró un escaneo válido, el
    /// código acumulado (sin el terminador). Un timestamp que retrocede reinicia la secuencia.
    /// </summary>
    /// <param name="c">Carácter de la tecla (se ignora si es terminador).</param>
    /// <param name="isTerminator">Si es la tecla que cierra el escaneo (Enter).</param>
    /// <param name="timestampMs">Instante de la tecla en milisegundos, con cualquier origen mientras sea monotónico.</param>
    public WedgeResult Push(char c, bool isTerminator, long timestampMs)
    {
        var gap = GapSinceLastKey(timestampMs);
        _lastTimestampMs = timestampMs;

        if (gap < 0 || gap > _options.IdleResetMs)
            _buffer.Clear();

        return isTerminator ? PushTerminator() : PushCharacter(c, gap);
    }

    /// <summary>Descarta lo acumulado: la próxima tecla vuelve a ser la primera de una secuencia.</summary>
    public void Reset() => _buffer.Clear();

    private long GapSinceLastKey(long timestampMs) =>
        _lastTimestampMs is { } last ? timestampMs - last : NoPreviousKey;

    private WedgeResult PushCharacter(char c, long gap)
    {
        // Primera tecla de la secuencia (todavía no hay gap que medir) o gap de tecleo manual: arranca de nuevo.
        if (_buffer.Length == 0 || gap > _options.InterKeyScanMaxMs)
        {
            _buffer.Clear().Append(c);
            return Pass;
        }

        // Pasado el tope ya no es un código: se deja de acumular (acota la memoria ante una tecla sostenida)
        // y las teclas vuelven a pasar al control.
        if (_buffer.Length > _options.MaxScanLength) return Pass;

        _buffer.Append(c);
        return _buffer.Length > _options.MaxScanLength ? Pass : Swallowed;
    }

    private WedgeResult PushTerminator()
    {
        var length = _buffer.Length;
        var result = length >= _options.MinScanLength && length <= _options.MaxScanLength
            ? new WedgeResult(WedgeAction.Swallow, _buffer.ToString())
            : Pass;

        _buffer.Clear();
        return result;
    }

    private static void Validate(WedgeOptions options)
    {
        if (options.InterKeyScanMaxMs <= 0)
            throw new ArgumentException("InterKeyScanMaxMs debe ser mayor que 0.", nameof(options));
        if (options.IdleResetMs < options.InterKeyScanMaxMs)
            throw new ArgumentException("IdleResetMs no puede ser menor que InterKeyScanMaxMs.", nameof(options));
        if (options.MinScanLength < 1)
            throw new ArgumentException("MinScanLength debe ser al menos 1.", nameof(options));
        if (options.MaxScanLength < options.MinScanLength)
            throw new ArgumentException("MaxScanLength no puede ser menor que MinScanLength.", nameof(options));
    }
}
