using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(WhisperLoom.App.Tests.TestAppBuilder))]

namespace WhisperLoom.App.Tests;

// Doku: Context7 /avaloniaui/avalonia-docs, docs/testing/headless-xunit.md (abgerufen 2026-10-30).
// Skia statt Headless-Drawing, damit Screenshots echte Pixel haben; Inter als feste Schrift auf jedem OS.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
