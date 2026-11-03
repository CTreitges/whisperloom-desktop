using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Die prüfbaren Ux-Regeln als Funktionen „Ansicht → Verstöße“ (leer = erfüllt). Die sechs Ux-Testklassen rufen sie je
/// Ansicht auf; <see cref="UxPendingTest"/> nutzt sie, um veraltete Einträge in <see cref="UxPending"/> zu finden.
/// Grundlage: Skill ui-ux-guidelines (02 §2.1–§2.4, 06 §6.3) und WCAG 2.2 AA; Desktop-Ausnahmen in docs/ux-exceptions.md.
/// </summary>
internal static class UxRules
{
    public const double EdgeTolerance = 1;
    public const double MinTarget = 24;

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    private static string Short(string? s) => s is null ? "" : s.Length <= 32 ? s.Replace('\n', ' ') : s[..31].Replace('\n', ' ') + "…";

    /// <summary>Richtlinie 02 §2.1: eine Textkante je Bereich (±1 px); umrandete Felder stehen mit dem Rand auf der Kante.</summary>
    public static List<string> TextEdges(Control root) =>
        [.. UxProbe.Edges(root)
            .Where(e => e.Control is not TextBlock t || t.TextAlignment is TextAlignment.Left or TextAlignment.Start)
            .GroupBy(e => e.Region)
            .SelectMany(region =>
            {
                var edge = region.Min(e => e.X);
                return region.Where(e => e.X - edge > EdgeTolerance)
                    .Select(e => $"{(e.Bordered ? "Rahmen" : "Text")} „{e.Sample}“ bei x={F(e.X)}, Kante x={F(edge)} (+{F(e.X - edge)} px)");
            })];

    /// <summary>WCAG 1.4.3 / 1.4.11: Text ≥ 4,5:1 (groß 3:1), Rahmen und Zustände von Bedienelementen ≥ 3:1.</summary>
    public static List<string> Contrast(Control root) =>
        [.. UxProbe.TextContrast(root).Where(c => c.Ratio < (c.Large ? 3 : 4.5))
                .Select(c => $"Text „{Short(c.Text.Text)}“ {F(c.Ratio)}:1 ({c.Foreground} auf {c.Background})")
            .Concat(UxProbe.UiContrast(root).Where(u => u.Ratio < 3)
                .Select(u => $"{u.Control.GetType().Name} {u.Part} {F(u.Ratio)}:1 ({u.Stroke} auf {u.Background})"))
            .Distinct()];

    /// <summary>WCAG 4.1.2: jedes bedienbare Element hat einen Namen (AutomationPeer).</summary>
    public static List<string> Names(Control root) =>
        [.. UxProbe.Targets(root).Where(t => string.IsNullOrWhiteSpace(t.Name))
            .Select(t => $"{t.Label} bei {F(t.Box.X)},{F(t.Box.Y)} ohne Namen")];

    /// <summary>WCAG 2.5.8 (AA): bedienbare Elemente mindestens 24 × 24 px (Desktop-Ausnahme D2 statt 44 px).</summary>
    public static List<string> TargetSize(Control root) =>
        [.. UxProbe.Targets(root).Where(t => t.Box.Width < MinTarget - 0.5 || t.Box.Height < MinTarget - 0.5)
            .Select(t => $"{t.Label} „{Short(t.Name)}“ {F(t.Box.Width)}×{F(t.Box.Height)} px")];

    /// <summary>
    /// WCAG 1.4.4: bei großer Schrift kein abgeschnittener oder verdeckter Text – kein Kürzen mit „…“, nichts waagrecht aus dem
    /// sichtbaren Bereich, nichts unter dem Fensterrand außerhalb eines rollenden Bereichs.
    /// </summary>
    public static List<string> Clipped(Control root)
    {
        var result = new List<string>();
        foreach (var t in UxProbe.Visible(root).OfType<TextBlock>().Where(t => !string.IsNullOrWhiteSpace(t.Text)))
        {
            var box = UxProbe.BoxIn(t, root);
            var name = $"„{Short(t.Text)}“";
            if (t.TextTrimming != TextTrimming.None && t.DesiredSize.Width > t.Bounds.Width + 1)
            {
                result.Add($"{name} gekürzt ({F(t.DesiredSize.Width)} > {F(t.Bounds.Width)} px)");
                continue;
            }
            foreach (var clip in t.GetVisualAncestors().OfType<Control>().Where(a => a.ClipToBounds && !ReferenceEquals(a, root)))
            {
                var c = UxProbe.BoxIn(clip, root);
                if (box.X < c.X - 1 || box.Right > c.Right + 1)
                {
                    result.Add($"{name} waagrecht abgeschnitten (x {F(box.X)}–{F(box.Right)}, sichtbar {F(c.X)}–{F(c.Right)})");
                    break;
                }
            }
            if (t.FindAncestorOfType<ScrollViewer>() is null && box.Bottom > root.Bounds.Height + 1)
            {
                result.Add($"{name} unter dem Fensterrand (y {F(box.Bottom)} > {F(root.Bounds.Height)})");
            }
        }
        return [.. result.Distinct()];
    }

    /// <summary>
    /// WCAG 2.1.1 / 2.4.7: jedes fokussierbare Bedienelement ist per Tab erreichbar (Listen: die Liste selbst ist der Tab-Stopp;
    /// Optionsfelder einer Gruppe: ein Feld der Gruppe ist der Tab-Stopp, die übrigen erreichen die Pfeiltasten – APG „Radio
    /// Group“) und zeigt beim Tastaturfokus einen Fokusrahmen.
    /// </summary>
    public static List<string> Keyboard(Control root)
    {
        var reached = UxFocus.TabOrder(root).ToHashSet();
        var reachedGroups = reached.OfType<RadioButton>().Select(r => r.GroupName).Where(g => !string.IsNullOrEmpty(g)).ToHashSet();
        var window = (Window)TopLevel.GetTopLevel(root)!;
        var result = new List<string>();
        // Je Liste genügt der erste Eintrag für den Fokusrahmen (alle Einträge teilen die Vorlage).
        var lists = new HashSet<ListBox>();
        foreach (var t in UxProbe.Targets(root).Where(t => t.Control.Focusable && t.Control.IsEffectivelyEnabled))
        {
            var stop = t.Control is ListBoxItem ? (Control?)t.Control.FindAncestorOfType<ListBox>() : t.Control;
            var ok = reached.Contains(t.Control) || (stop is not null && (reached.Contains(stop)
                || stop.GetVisualDescendants().OfType<ListBoxItem>().Any(reached.Contains)))
                || (t.Control is RadioButton { GroupName: { Length: > 0 } group } && reachedGroups.Contains(group));
            if (!ok)
            {
                result.Add($"{t.Label} „{Short(t.Name)}“ per Tab nicht erreichbar");
                continue;
            }
            if (t.Control is ListBoxItem && t.Control.FindAncestorOfType<ListBox>() is { } list && !lists.Add(list))
            {
                continue;
            }
            if (!UxFocus.FocusVisible(window, t.Control))
            {
                result.Add($"{t.Label} „{Short(t.Name)}“ ohne sichtbaren Fokusrahmen");
            }
        }
        return [.. result.Distinct()];
    }
}
