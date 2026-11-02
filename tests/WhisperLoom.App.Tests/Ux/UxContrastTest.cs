using Avalonia.Headless.XUnit;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// Kontrast in jeder Ansicht und jeder Variante (hell, dunkel; Hochkontrast mit UX-1): Text ≥ 4,5:1 (groß 3:1), Rahmen und
/// Zustände von Bedienelementen ≥ 3:1 (WCAG 1.4.3, 1.4.11). Ausstehendes: <see cref="UxPending"/> (Schlüssel „Ansicht/Variante“).
/// </summary>
public sealed class UxContrastTest
{
    public static IEnumerable<object[]> Cases =>
        UxViews.All.SelectMany(v => UxViews.Themes.Select(t => new object[] { v.Id, t.Name }));

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Holds_in_every_view_and_theme(string view, string theme)
    {
        UxPending.SkipIfPending(nameof(UxContrastTest), $"{view}/{theme}");
        using var scene = await UxViews.Get(view).Build(UxViews.Themes.Single(t => t.Name == theme).Theme);
        UxStage.Settle(scene.Window);
        var violations = UxRules.Contrast(scene.Target);
        Assert.True(violations.Count == 0, $"{view}/{theme}: " + string.Join(Environment.NewLine, violations));
    }
}
