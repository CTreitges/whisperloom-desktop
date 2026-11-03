using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Jedes bedienbare Element hat einen Namen über den AutomationPeer (WCAG 4.1.2). Liste der Ansichten: <see cref="UxViews"/>; Ausstehendes: <see cref="UxPending"/>.</summary>
public sealed class UxAccessibilityTest
{
    public static IEnumerable<object[]> Views => UxViews.Ids;

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public async Task Holds_in_every_view(string view)
    {
        UxPending.SkipIfPending(nameof(UxAccessibilityTest), view);
        using var scene = await UxViews.Get(view).Build(ThemeVariant.Dark);
        UxStage.Settle(scene.Window);
        var violations = UxRules.Names(scene.Target);
        Assert.True(violations.Count == 0, $"{view}: " + string.Join(Environment.NewLine, violations));
    }
}
