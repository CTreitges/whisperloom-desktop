using System.Globalization;
using System.Text;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Messbericht je Ansicht als Beleg fürs Audit (<c>WLD_UX_REPORT</c> = Zieldatei, Markdown): Klickziele unter 24 px, Elemente
/// ohne Namen, Platzhalter, Schriftgrößen, Textkanten je Bereich, Texte unter 4,5:1 (große unter 3:1) je Variante.
/// </summary>
public sealed class UxReportTest
{
    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    [AvaloniaFact]
    public async Task Write_measurement_report()
    {
        var path = Environment.GetEnvironmentVariable("WLD_UX_REPORT");
        if (path is null)
        {
            Assert.Skip("WLD_UX_REPORT nicht gesetzt");
            return;
        }
        var before = Loc.Instance.Language;
        Loc.Instance.SetLanguage("de");
        var md = new StringBuilder("# Ux-Messbericht\n\n");
        try
        {
            foreach (var view in UxViews.All)
            {
                md.Append(CultureInfo.InvariantCulture, $"## {view.Id} ({view.File})\n\n");
                foreach (var (name, theme) in UxViews.Themes)
                {
                    using var scene = await view.Build(theme);
                    UxStage.Settle(scene.Window);
                    var root = scene.Target;
                    if (name == "dark")
                    {
                        Structure(md, root);
                        await WriteBoxesAsync(view.Id, root);
                    }
                    var low = UxProbe.TextContrast(root).Where(c => c.Ratio < (c.Large ? 3 : 4.5)).ToList();
                    md.Append(CultureInfo.InvariantCulture, $"- Kontrast {name}: {(low.Count == 0 ? "alle Texte ok" : $"{low.Count} unter Grenze")}\n");
                    foreach (var c in low.DistinctBy(c => (c.Text.Text, c.Foreground, c.Background)).Take(12))
                    {
                        md.Append(CultureInfo.InvariantCulture,
                            $"  - {F(c.Ratio)}:1 „{Short(c.Text.Text)}“ {c.Foreground} auf {c.Background}, {F(c.Text.FontSize)} px\n");
                    }
                    // WLD_UX_REPORT_ALL=1: alle Rahmen-Werte, nicht nur die unter 3:1 (Prüfen der Messung).
                    var all = Environment.GetEnvironmentVariable("WLD_UX_REPORT_ALL") == "1";
                    var ui = UxProbe.UiContrast(root).Where(u => all || u.Ratio < 3).ToList();
                    md.Append(CultureInfo.InvariantCulture, $"- Rahmen {name}: {(ui.Count == 0 ? "alle ≥ 3:1" : $"{ui.Count} unter 3:1")}\n");
                    foreach (var u in ui.DistinctBy(u => (u.Control.GetType(), u.Part, u.Stroke)).Take(8))
                    {
                        md.Append(CultureInfo.InvariantCulture, $"  - {F(u.Ratio)}:1 {u.Control.GetType().Name} {u.Part} {u.Stroke} auf {u.Background}\n");
                    }
                }
                md.Append('\n');
            }
            await File.WriteAllTextAsync(path, md.ToString());
        }
        finally
        {
            Loc.Instance.SetLanguage(before);
        }
    }

    /// <summary>
    /// <c>WLD_UX_BOXES</c> = Ordner: je Ansicht <c>&lt;id&gt;.json</c> mit Lage und Größe aller Texte und bedienbaren Elemente
    /// (Pixel im Screenshot) – für nummerierte Marker auf den Vorher-Bildern der Mockups.
    /// </summary>
    private static async Task WriteBoxesAsync(string id, Avalonia.Controls.Control root)
    {
        var dir = Environment.GetEnvironmentVariable("WLD_UX_BOXES");
        if (dir is null)
        {
            return;
        }
        Directory.CreateDirectory(dir);
        var texts = UxProbe.Visible(root).OfType<Avalonia.Controls.TextBlock>().Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => (Kind: "text", Name: t.Name ?? "", Text: t.Text!, Box: UxProbe.BoxIn(t, root)));
        var targets = UxProbe.Targets(root).Select(t => (Kind: t.Kind, Name: t.Control.Name ?? "", Text: t.Name, Box: t.Box));
        var items = texts.Concat(targets).Select(e => new Dictionary<string, object>
        {
            ["kind"] = e.Kind, ["name"] = e.Name, ["text"] = e.Text,
            ["x"] = Math.Round(e.Box.X), ["y"] = Math.Round(e.Box.Y), ["w"] = Math.Round(e.Box.Width), ["h"] = Math.Round(e.Box.Height),
        });
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"),
            System.Text.Json.JsonSerializer.Serialize(items, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    private static string Short(string? s) => s is null ? "" : s.Length <= 40 ? s.Replace('\n', ' ') : s[..39].Replace('\n', ' ') + "…";

    private static void Structure(StringBuilder md, Avalonia.Controls.Control root)
    {
        var targets = UxProbe.Targets(root);
        var small = targets.Where(t => t.Box.Width < 24 || t.Box.Height < 24).ToList();
        var unnamed = targets.Where(t => string.IsNullOrWhiteSpace(t.Name)).ToList();
        md.Append(CultureInfo.InvariantCulture, $"- Bedienbare Elemente: {targets.Count}; unter 24 px: {small.Count}; ohne Namen: {unnamed.Count}\n");
        foreach (var t in small.Take(10))
        {
            md.Append(CultureInfo.InvariantCulture, $"  - klein: {t.Label} {F(t.Box.Width)}×{F(t.Box.Height)} „{Short(t.Name)}“\n");
        }
        foreach (var t in unnamed.Take(10))
        {
            md.Append(CultureInfo.InvariantCulture, $"  - ohne Namen: {t.Label} bei {F(t.Box.X)},{F(t.Box.Y)} ({F(t.Box.Width)}×{F(t.Box.Height)})\n");
        }
        var placeholders = UxProbe.Placeholders(root);
        if (placeholders.Count > 0)
        {
            md.Append(CultureInfo.InvariantCulture, $"- Platzhalter: {string.Join(" · ", placeholders.Select(p => $"„{Short(p.PlaceholderText)}“"))}\n");
        }
        md.Append(CultureInfo.InvariantCulture,
            $"- Schriftgrößen: {string.Join(", ", UxProbe.FontSizes(root).Select(g => $"{F(g.Key)} px ×{g.Count()}"))}\n");
        foreach (var region in UxProbe.Edges(root).GroupBy(e => e.Region))
        {
            var xs = region.GroupBy(e => Math.Round(e.X)).OrderBy(g => g.Key)
                .Select(g => $"{F(g.Key)} ({g.Count()}: {string.Join(", ", g.Take(3).Select(e => (e.Bordered ? "▭" : "") + e.Sample))})");
            md.Append(CultureInfo.InvariantCulture, $"- Kanten [{(region.Key == "main" ? "Hauptbereich" : region.Key)}]: {string.Join(" · ", xs)}\n");
        }
        // Tab-Reihenfolge mit Namen (Grundlage für die Narrator-Liste in H2).
        var order = UxFocus.TabOrder(root).OfType<Avalonia.Controls.Control>()
            .Select(c => $"{c.GetType().Name}{(string.IsNullOrEmpty(c.Name) ? "" : "#" + c.Name)} „{Short(UxProbe.NameOf(c))}“");
        md.Append(CultureInfo.InvariantCulture, $"- Tab-Reihenfolge: {string.Join(" → ", order)}\n");
    }
}
