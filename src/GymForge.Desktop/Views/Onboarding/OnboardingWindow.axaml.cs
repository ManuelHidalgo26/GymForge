using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GymForge.Desktop.Views.Onboarding;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
