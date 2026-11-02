using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Eine aufgebaute Ansicht: Fenster, geprüfter Bereich (meist das Fenster selbst), Aufräumen und – wenn der Inhalt in ein
/// Bildfenster umgezogen ist – der Typ des Originalfensters.</summary>
internal sealed record UxScene(Window Window, Control Target, IDisposable? Cleanup, Type? Origin = null) : IDisposable
{
    public void Dispose()
    {
        Window.Close();
        Cleanup?.Dispose();
    }
}

/// <summary>
/// Eine Ansicht der App für Screenshots, Audit und die Ux-Tests (eine Liste für alles, Goal UX Akzeptanz 6).
/// <paramref name="Group"/> = Fenstergruppe (Mockups, PR-Schnitt), <paramref name="File"/> = die geprüfte <c>.axaml</c>.
/// </summary>
internal sealed record UxView(string Id, string Group, string File, Func<ThemeVariant, Task<UxScene>> Build)
{
    public override string ToString() => Id;
}

/// <summary>Gemeinsame Helfer der Szenen: Layout abwarten, Höhe an den Inhalt anpassen, Hintergrund für schwebende Fenster.</summary>
internal static class UxStage
{
    /// <summary>Größte Fensterhöhe beim Anpassen an den Inhalt (lange Einstellungsseiten ganz im Bild).</summary>
    public const double MaxHeight = 2400;

    public static void Settle(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        // Übergänge (Pfeil von „Erweitert“, Fokus, Fortschritt) zu Ende laufen lassen.
        Thread.Sleep(250);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    /// <summary>
    /// Vergrößert das Fenster, bis der erste rollende Bereich nichts mehr verbirgt (höchstens <see cref="MaxHeight"/>), damit
    /// Bild und Messungen die ganze Ansicht sehen.
    /// </summary>
    public static void FitHeight(Window window)
    {
        Settle(window);
        for (var i = 0; i < 3; i++)
        {
            var hidden = window.GetVisualDescendants().OfType<ScrollViewer>()
                .Where(s => s.IsEffectivelyVisible)
                .Select(s => s.Extent.Height - s.Viewport.Height)
                .DefaultIfEmpty(0)
                .Max();
            if (hidden <= 1 || window.Height >= MaxHeight)
            {
                return;
            }
            window.Height = Math.Min(MaxHeight, Math.Ceiling(window.Height + hidden + 2));
            Settle(window);
        }
    }

    public static IBrush Brush(ThemeVariant theme, string key) =>
        (IBrush)Application.Current!.FindResource(theme, key)!;

    /// <summary>
    /// Variante der Fenster, die nicht dem System folgen (Bühne, Overlay, Pille, Hinweis, Markierung, Update-Nachfrage): Die App
    /// steht global auf Dunkel (<c>App.axaml</c>), nur Fenster mit <c>SystemTheme.Follow</c> wechseln. Die Szene bildet das nach.
    /// </summary>
    public static readonly ThemeVariant AppFixed = ThemeVariant.Dark;

    /// <summary>
    /// Schwebende Fenster (Overlay, Pille, Hinweis) sind transparent und würden schwarz gerendert. Für das Bild liegt der
    /// Hintergrund der Variante darunter – so sieht man sie wie über einer hellen bzw. dunklen Anwendung. Das Fenster selbst
    /// bleibt in <see cref="AppFixed"/>.
    /// </summary>
    public static T OnBackdrop<T>(T window, ThemeVariant backdrop) where T : Window
    {
        window.Background = Brush(backdrop, "Loom.Background");
        window.RequestedThemeVariant = AppFixed;
        return window;
    }

    /// <summary>
    /// Nimmt den Inhalt eines Fensters heraus und legt ihn mit Rand auf den Hintergrund der Variante <paramref name="backdrop"/>;
    /// der Inhalt bleibt in <see cref="AppFixed"/> wie zur Laufzeit.
    /// </summary>
    public static Window Hosted(Window source, ThemeVariant backdrop, double width, double height, Thickness margin)
    {
        var content = (Control)source.Content!;
        var context = source.DataContext;
        source.Content = null;
        content.DataContext = context;
        content.Margin = margin;
        content.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        content.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        return new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Background = Brush(backdrop, "Loom.Background"),
            RequestedThemeVariant = AppFixed,
        };
    }
}
