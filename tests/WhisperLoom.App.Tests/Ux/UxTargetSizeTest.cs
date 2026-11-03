using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Bedienbare Elemente mindestens 24 × 24 px (WCAG 2.5.8, Desktop-Ausnahme D2). Liste der Ansichten: <see cref="UxViews"/>; Ausstehendes: <see cref="UxPending"/>.</summary>
public sealed class UxTargetSizeTest
{
    public static IEnumerable<object[]> Views => UxViews.Ids;

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public async Task Holds_in_every_view(string view)
    {
        UxPending.SkipIfPending(nameof(UxTargetSizeTest), view);
        using var scene = await UxViews.Get(view).Build(ThemeVariant.Dark);
        UxStage.Settle(scene.Window);
        var violations = UxRules.TargetSize(scene.Target);
        Assert.True(violations.Count == 0, $"{view}: " + string.Join(Environment.NewLine, violations));
    }
}
