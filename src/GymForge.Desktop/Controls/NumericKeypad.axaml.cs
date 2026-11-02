using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;

namespace GymForge.Desktop.Controls;

/// <summary>
/// Teclado numérico en pantalla para recepción táctil (DNI, credencial, PIN). Edita un string bindeable
/// (<see cref="Text"/>, two-way) y avisa con <see cref="Entered"/> al tocar aceptar: qué hacer con el valor lo
/// decide quien lo usa. No conoce ningún ViewModel.
/// </summary>
public partial class NumericKeypad : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<NumericKeypad, string>(
            nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay,
            coerce: static (_, value) => value ?? string.Empty);   // un binding puede dejarlo en null

    public static readonly StyledProperty<int> MaxLengthProperty =
        AvaloniaProperty.Register<NumericKeypad, int>(nameof(MaxLength), 20);

    /// <summary>Muestra un punto por carácter (PIN) en vez del valor. El texto guardado no cambia.</summary>
    public static readonly StyledProperty<bool> MaskProperty =
        AvaloniaProperty.Register<NumericKeypad, bool>(nameof(Mask));

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public int MaxLength
    {
        get => GetValue(MaxLengthProperty);
        set => SetValue(MaxLengthProperty, value);
    }

    public bool Mask
    {
        get => GetValue(MaskProperty);
        set => SetValue(MaskProperty, value);
    }

    /// <summary>Se tocó el botón de aceptar.</summary>
    public event EventHandler? Entered;

    public NumericKeypad()
    {
        InitializeComponent();
        RefreshDisplay();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == MaskProperty)
            RefreshDisplay();
    }

    public void PressDigit(char digit) => Text = KeypadText.Append(Text, digit, MaxLength);

    public void PressBackspace() => Text = KeypadText.Backspace(Text);

    public void PressEnter() => Entered?.Invoke(this, EventArgs.Empty);

    private void RefreshDisplay() => Display.Text = KeypadText.Display(Text, Mask);

    private void OnDigitClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string { Length: 1 } digit })
            PressDigit(digit[0]);
    }

    private void OnBackspaceClick(object? sender, RoutedEventArgs e) => PressBackspace();

    private void OnEnterClick(object? sender, RoutedEventArgs e) => PressEnter();
}
