using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Alles per Tab erreichbar, Fokus sichtbar (WCAG 2.1.1, 2.4.7); fokusfreie Fenster nach Ausnahme D5 ausgenommen. Liste der Ansichten: <see cref="UxViews"/>; Ausstehendes: <see cref="UxPending"/>.</summary>
public sealed class UxKeyboardTest
{
    public static IEnumerable<object[]> Views => UxViews.Ids;

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public async Task Holds_in_every_view(string view)
    {
        UxPending.SkipIfPending(nameof(UxKeyboardTest), view);
        using var scene = await UxViews.Get(view).Build(ThemeVariant.Dark);
        UxStage.Settle(scene.Window);
        var violations = UxRules.Keyboard(scene.Target);
        Assert.True(violations.Count == 0, $"{view}: " + string.Join(Environment.NewLine, violations));
    }
}
