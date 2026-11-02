using Avalonia.Controls;
using Avalonia.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentAssertions;
using GymForge.Desktop.Controls;

namespace GymForge.Desktop.Tests;

/// <summary>
/// La edición del texto (<see cref="KeypadText"/>, pura) y el control <see cref="NumericKeypad"/> armado sin ventana.
/// Cómo se ve y su integración en el kiosk y en el login de caja se verifican a mano.
/// </summary>
public class NumericKeypadTests
{
    [Theory]
    [InlineData("", '4', 20, "4")]
    [InlineData("12", '3', 20, "123")]
    [InlineData("123", '0', 20, "1230")]
    public void Append_AddsTheDigitAtTheEnd(string text, char digit, int max, string expected)
    {
        KeypadText.Append(text, digit, max).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234", 4)]
    [InlineData("12345678", 8)]
    public void Append_AtMaxLength_IgnoresTheDigit(string text, int max)
    {
        KeypadText.Append(text, '9', max).Should().Be(text);
    }

    [Fact]
    public void Append_OneBelowMaxLength_StillAdds()
    {
        KeypadText.Append("123", '4', 4).Should().Be("1234");
    }

    [Theory]
    [InlineData('a')]
    [InlineData('-')]
    [InlineData(' ')]
    [InlineData('٣')]   // dígito arábigo: no es ASCII
    public void Append_IgnoresWhatIsNotADigit(char notADigit)
    {
        KeypadText.Append("12", notADigit, 20).Should().Be("12");
    }

    [Theory]
    [InlineData("1234", "123")]
    [InlineData("1", "")]
    [InlineData("", "")]
    public void Backspace_RemovesTheLastCharacter(string text, string expected)
    {
        KeypadText.Backspace(text).Should().Be(expected);
    }

    [Theory]
    [InlineData("1234", false, "1234")]
    [InlineData("1234", true, "●●●●")]
    [InlineData("", true, "")]
    public void Display_OnlyMasksWhatIsShown(string text, bool mask, string expected)
    {
        KeypadText.Display(text, mask).Should().Be(expected);
    }

    [Fact]
    public void Mask_DoesNotChangeTheEditedText()
    {
        var typed = KeypadText.Append(KeypadText.Append("", '1', 8), '2', 8);

        KeypadText.Display(typed, mask: true).Should().Be("●●");
        typed.Should().Be("12");
    }

    // ── El control ────────────────────────────────────────────────────────────

    [Fact]
    public void Control_DigitsAppend_AndBackspaceRemovesTheLast()
    {
        var keypad = new NumericKeypad();

        keypad.PressDigit('1');
        keypad.PressDigit('2');
        keypad.PressDigit('3');
        keypad.PressBackspace();

        keypad.Text.Should().Be("12");
    }

    [Fact]
    public void Control_RespectsMaxLength()
    {
        var keypad = new NumericKeypad { MaxLength = 3 };

        foreach (var digit in "98765") keypad.PressDigit(digit);

        keypad.Text.Should().Be("987");
    }

    [Fact]
    public void Control_Enter_RaisesEnteredOnce_AndLeavesTheTextAlone()
    {
        var keypad = new NumericKeypad { Text = "30111222" };
        var raised = 0;
        keypad.Entered += (_, _) => raised++;

        keypad.PressEnter();

        raised.Should().Be(1);
        keypad.Text.Should().Be("30111222");
    }

    [Fact]
    public void Control_Mask_ChangesTheDisplayButNotTheText()
    {
        var keypad = new NumericKeypad { Mask = true };

        keypad.PressDigit('1');
        keypad.PressDigit('2');

        keypad.Text.Should().Be("12");
        keypad.FindControl<TextBlock>("Display")!.Text.Should().Be("●●");

        keypad.Mask = false;
        keypad.FindControl<TextBlock>("Display")!.Text.Should().Be("12");
    }

    [Fact]
    public void Control_Display_FollowsTextChangesMadeOutsideTheKeypad()
    {
        var keypad = new NumericKeypad();

        keypad.Text = "4567";

        keypad.FindControl<TextBlock>("Display")!.Text.Should().Be("4567");
    }

    [Fact]
    public void Control_NullText_IsTreatedAsEmpty()
    {
        var keypad = new NumericKeypad { Text = "12" };

        keypad.Text = null!;

        keypad.Text.Should().BeEmpty();
        keypad.FindControl<TextBlock>("Display")!.Text.Should().BeEmpty();
    }

    [Fact]
    public void Control_Text_BindsTwoWayToAViewModelProperty()
    {
        var holder = new PinHolder { Pin = "12" };
        var keypad = new NumericKeypad();
        keypad.Bind(NumericKeypad.TextProperty, new Binding(nameof(PinHolder.Pin)) { Source = holder });

        keypad.Text.Should().Be("12", "el valor del VM llega al teclado");
        keypad.PressDigit('3');

        holder.Pin.Should().Be("123", "lo que se toca en el teclado llega al VM");
    }
}

/// <summary>Origen de datos mínimo para probar el binding two-way del teclado.</summary>
internal sealed partial class PinHolder : ObservableObject
{
    [ObservableProperty] private string _pin = string.Empty;
}
