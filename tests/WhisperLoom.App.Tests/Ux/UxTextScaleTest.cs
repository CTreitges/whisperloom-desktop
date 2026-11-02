using Avalonia.Headless.XUnit;
using Avalonia.Styling;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Bei 200 % Textgröße kein abgeschnittener oder verdeckter Text (WCAG 1.4.4). Liste der Ansichten: <see cref="UxViews"/>; Ausstehendes: <see cref="UxPending"/>.</summary>
public sealed class UxTextScaleTest
{
    public static IEnumerable<object[]> Views => UxViews.Ids;

    [AvaloniaTheory]
    [MemberData(nameof(Views))]
    public async Task Holds_in_every_view(string view)
    {
        UxPending.SkipIfPending(nameof(UxTextScaleTest), view);
        Design.TextScale.Apply(Avalonia.Application.Current!, 2.0);
        try
        {
            using var scene = await UxViews.Get(view).Build(ThemeVariant.Dark);
            UxStage.Settle(scene.Window);
            var violations = UxRules.Clipped(scene.Target);
            Assert.True(violations.Count == 0, $"{view}: " + string.Join(Environment.NewLine, violations));
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
        }
    }
}
