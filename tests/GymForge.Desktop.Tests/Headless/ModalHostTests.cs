using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using FluentAssertions;
using GymForge.Desktop.Controls;

namespace GymForge.Desktop.Tests.Headless;

/// <summary>
/// Foco y teclado del <see cref="ModalHost"/>. Lo que NO es verificable en headless y queda para la prueba manual
/// en la app viva: el recorrido real del Tab cíclico tecla a tecla (<c>TabNavigation="Cycle"</c> del XAML),
/// el anillo visual de <c>:focus-visible</c> y el foco por teclado en las grillas.
/// </summary>
public class ModalHostTests
{
    private sealed class Fixture
    {
        public required Window Window { get; init; }
        public required ModalHost Host { get; init; }
        public required TextBox Outside { get; init; }
        public required TextBox Inside { get; init; }
        public required Button Cancel { get; init; }
        public int CancelClicks { get; set; }
    }

    private static Fixture CreateOpenable()
    {
        var outside = new TextBox { Name = "outside" };
        var inside = new TextBox { Name = "inside" };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        var host = new ModalHost { IsVisible = false, Content = new StackPanel { Children = { inside, cancel } } };
        var window = new Window { Content = new Panel { Children = { outside, host } } };
        var fixture = new Fixture { Window = window, Host = host, Outside = outside, Inside = inside, Cancel = cancel };
        cancel.Click += (_, _) =>
        {
            fixture.CancelClicks++;
            host.IsVisible = false;
        };

        window.Show();
        outside.Focus();
        return fixture;
    }

    private static void Open(Fixture f)
    {
        f.Host.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void AlAbrir_ElFocoEntraAlPrimerControlDeLaCard()
    {
        var f = CreateOpenable();

        Open(f);

        f.Window.FocusManager!.GetFocusedElement().Should().BeSameAs(f.Inside);
    }

    [AvaloniaFact]
    public void AlCerrar_ElFocoVuelveAQuienLoTeniaAntes()
    {
        var f = CreateOpenable();
        Open(f);

        f.Host.IsVisible = false;
        Dispatcher.UIThread.RunJobs();

        f.Window.FocusManager!.GetFocusedElement().Should().BeSameAs(f.Outside);
    }

    [AvaloniaFact]
    public void Escape_ActivaElBotonIsCancel_YCierraElModal()
    {
        var f = CreateOpenable();
        Open(f);

        f.Window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        f.CancelClicks.Should().Be(1);
        f.Host.IsVisible.Should().BeFalse();
        f.Window.FocusManager!.GetFocusedElement().Should().BeSameAs(f.Outside);
    }
}
