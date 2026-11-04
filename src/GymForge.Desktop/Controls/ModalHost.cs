using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace GymForge.Desktop.Controls;

/// <summary>
/// Host común de los modales: scrim a pantalla completa, card (<c>Border.ModalCard</c>) centrada con scroll de
/// reserva y comportamiento de teclado parejo. Al mostrarse lleva el foco al primer control de la card (si el
/// contenido no lo tomó ya), el Tab cicla dentro de la card y al ocultarse devuelve el foco a quien lo tenía.
/// Escape lo resuelve cada modal con su botón <c>IsCancel</c>. La visibilidad se bindea con <c>IsVisible</c>.
/// </summary>
public class ModalHost : ContentControl
{
    /// <summary>Ancho máximo de la card (el command palette es más ancho que un formulario).</summary>
    public static readonly StyledProperty<double> CardMaxWidthProperty =
        AvaloniaProperty.Register<ModalHost, double>(nameof(CardMaxWidth), 440);

    private IInputElement? _focusBeforeOpen;

    public double CardMaxWidth
    {
        get => GetValue(CardMaxWidthProperty);
        set => SetValue(CardMaxWidthProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;

        if (change.GetNewValue<bool>())
            Open();
        else
            Close();
    }

    private void Open()
    {
        _focusBeforeOpen = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        Dispatcher.UIThread.Post(FocusFirstControl, DispatcherPriority.Background);
    }

    private void Close()
    {
        var previous = _focusBeforeOpen;
        _focusBeforeOpen = null;
        if (previous is Visual { IsEffectivelyVisible: true } && previous.Focusable)
            previous.Focus();
    }

    private void FocusFirstControl()
    {
        if (!IsVisible || FocusAlreadyInside()) return;

        this.GetVisualDescendants()
            .OfType<InputElement>()
            .FirstOrDefault(IsFocusCandidate)
            ?.Focus();
    }

    private bool FocusAlreadyInside() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is Visual focused && this.IsVisualAncestorOf(focused);

    private static bool IsFocusCandidate(InputElement element) =>
        element is { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
        && KeyboardNavigation.GetIsTabStop(element);
}
