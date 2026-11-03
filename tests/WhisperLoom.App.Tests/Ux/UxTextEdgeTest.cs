using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Eine Textkante je Ansicht (±1 px, Richtlinie 02 §2.1) – in jeder Ansicht, dunkel. Liste der Ansichten: <see cref="UxViews"/>; Ausstehendes: <see cref="UxPending"/>.</summary>
public sealed class UxTextEdgeTest
{
    public static IEnumerable<object[]> Views => UxViews.Ids;

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public async Task Holds_in_every_view(string view)
    {
        UxPending.SkipIfPending(nameof(UxTextEdgeTest), view);
        using var scene = await UxViews.Get(view).Build(ThemeVariant.Dark);
        UxStage.Settle(scene.Window);
        var violations = UxRules.TextEdges(scene.Target);
        Assert.True(violations.Count == 0, $"{view}: " + string.Join(Environment.NewLine, violations));
    }
}
