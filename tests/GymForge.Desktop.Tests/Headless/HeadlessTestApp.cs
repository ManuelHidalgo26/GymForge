using Avalonia;
using Avalonia.Headless;
using GymForge.Desktop;
using GymForge.Desktop.Tests.Headless;

[assembly: AvaloniaTestApplication(typeof(HeadlessTestApp))]

namespace GymForge.Desktop.Tests.Headless;

/// <summary>App real de GymForge (temas y estilos incluidos) sobre la plataforma headless, para tests de controles.</summary>
public static class HeadlessTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
