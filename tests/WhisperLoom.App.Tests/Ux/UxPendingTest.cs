using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Jeder Eintrag in <see cref="UxPending"/> verletzt seine Regel noch – sonst muss er raus (die Liste soll nur schrumpfen).
/// Maßgeblich ist das Rendering unter Windows; auf anderen Systemen wird ein dort haltender Eintrag übersprungen.
/// </summary>
public sealed class UxPendingTest
{
    public static IEnumerable<object[]> Entries =>
        UxPending.Rules.SelectMany(r => r.Value.Keys.Select(v => new object[] { r.Key, v })).DefaultIfEmpty(["", ""]);

    [AvaloniaTheory]
    [MemberData(nameof(Entries))]
    public async Task Pending_entry_still_violates_its_rule(string rule, string entry)
    {
        if (rule.Length == 0)
        {
            return; // nichts ausstehend
        }
        var parts = entry.Split('/');
        var theme = parts.Length > 1 ? UxViews.Themes.Single(t => t.Name == parts[1]).Theme : ThemeVariant.Dark;
        if (rule == nameof(UxTextScaleTest))
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 2.0);
        }
        try
        {
            using var scene = await UxViews.Get(parts[0]).Build(theme);
            UxStage.Settle(scene.Window);
            var violations = rule switch
            {
                nameof(UxTextEdgeTest) => UxRules.TextEdges(scene.Target),
                nameof(UxContrastTest) => UxRules.Contrast(scene.Target),
                nameof(UxAccessibilityTest) => UxRules.Names(scene.Target),
                nameof(UxTargetSizeTest) => UxRules.TargetSize(scene.Target),
                nameof(UxKeyboardTest) => UxRules.Keyboard(scene.Target),
                nameof(UxTextScaleTest) => UxRules.Clipped(scene.Target),
                _ => throw new InvalidOperationException("Unbekannte Regel " + rule),
            };
            if (violations.Count == 0 && !OperatingSystem.IsWindows())
            {
                // Die Liste beschreibt das Windows-Rendering (Segoe UI); unter Linux rendern andere Schriften mit anderen Maßen,
                // ein Eintrag kann dort zufällig halten (CI ubuntu: settings-output/Textkante). Streng prüft der Windows-Lauf.
                Assert.Skip($"{entry} erfüllt die Regel hier, aber nicht unter Windows");
            }
            Assert.True(violations.Count > 0, $"{rule}: {entry} erfüllt die Regel – Eintrag aus UxPending entfernen");
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
        }
    }
}
