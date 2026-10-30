using Avalonia;

namespace WhisperLoom.App;

internal static class Program
{
    // Doku: Context7 /avaloniaui/avalonia-docs, docs/fundamentals/application-lifetimes.md (abgerufen 2026-10-30).
    // Vor BuildAvaloniaApp keine Avalonia-Typen verwenden: der SynchronizationContext steht noch nicht.
    [STAThread]
    public static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Wird auch vom Designer-Previewer genutzt.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect();
}
