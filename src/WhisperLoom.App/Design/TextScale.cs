using Avalonia;
using Avalonia.Controls;

namespace WhisperLoom.App.Design;

/// <summary>
/// Textgröße des Systems (WCAG 1.4.4, bis 200 %): multipliziert alle Schrift-Tokens <c>Loom.Font.*</c> und die
/// Schriftgrößen des Fluent-Themes mit dem Faktor. Die Grundwerte stammen aus <c>Tokens.axaml</c> bzw. dem Theme und werden
/// beim ersten Anwenden gemerkt; die skalierten Werte liegen in <c>Application.Resources</c> und gewinnen damit über die
/// eingebundenen Wörterbücher. Steuerelemente lesen sie per <c>DynamicResource</c>, also wirkt ein neuer Faktor sofort.
/// Doku: docs.avaloniaui.net/docs/app-development/resources (Lookup-Reihenfolge, abgerufen 2026-11-02).
/// </summary>
public static class TextScale
{
    /// <summary>Schriftgrößen des Fluent-Themes (Avalonia.Themes.Fluent 12.1.4: Inhalt der Steuerelemente, Tooltips).</summary>
    public static readonly IReadOnlyList<string> FluentKeys = ["ControlContentThemeFontSize", "ToolTipContentThemeFontSize"];

    private static readonly Dictionary<string, double> Base = new(StringComparer.Ordinal);

    /// <summary>Zuletzt angewandter Faktor.</summary>
    public static double Current { get; private set; } = 1.0;

    /// <summary>Schrift-Tokens aus <c>Tokens.axaml</c> (Schlüssel beginnen mit <c>Loom.Font.</c>; Abdeckung prüft TextScaleTest).</summary>
    public static IEnumerable<string> TokenKeys(Application app) =>
        app.Resources.MergedDictionaries.OfType<IResourceDictionary>()
            .Concat(app.Resources.MergedDictionaries.OfType<Avalonia.Markup.Xaml.Styling.ResourceInclude>().Select(r => r.Loaded).OfType<IResourceDictionary>())
            .SelectMany(d => d.Keys.OfType<string>())
            .Where(k => k.StartsWith("Loom.Font.", StringComparison.Ordinal))
            .Distinct();

    /// <summary>Wendet <paramref name="factor"/> (1,0–2,25) auf alle Schriftgrößen an.</summary>
    public static void Apply(Application app, double factor)
    {
        factor = SystemPreferences.Clamp(factor);
        foreach (var key in TokenKeys(app).Concat(FluentKeys))
        {
            if (!Base.TryGetValue(key, out var size))
            {
                if (!app.TryGetResource(key, null, out var value) || value is not double found)
                {
                    continue;
                }
                Base[key] = size = found;
            }
            app.Resources[key] = Math.Round(size * factor, 2);
        }
        Current = factor;
    }

    /// <summary>
    /// Übernimmt die Textgröße des Systems (beim Start und beim Öffnen eines Fensters); tut nichts, wenn sie gleich geblieben ist.
    /// Gibt den angewandten Faktor zurück.
    /// </summary>
    public static double Follow(Application app, ISystemPreferences preferences)
    {
        var factor = SystemPreferences.Clamp(preferences.TextScale);
        if (factor != Current || Base.Count == 0)
        {
            Apply(app, factor);
        }
        return factor;
    }

    /// <summary>Grundwert eines Schlüssels (vor dem Skalieren), z. B. für Tests.</summary>
    public static double? BaseOf(string key) => Base.TryGetValue(key, out var size) ? size : null;
}
