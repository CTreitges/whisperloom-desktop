using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>
/// TP-WSETTINGS: Seitennamen in der Seitenleiste der Einstellungen brechen nur an Wortgrenzen um – bei 100 % und 200 %
/// Textgröße, hell und dunkel, deutsch und englisch (vorher bei 200 %: „Verbindun|gen“, „Datensch|utz“).
/// </summary>
public sealed class UxSidebarWordBreakTest
{
    public static TheoryData<double, string, string> Cases()
    {
        var data = new TheoryData<double, string, string>();
        foreach (var scale in new[] { 1.0, 2.0 })
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                foreach (var lang in new[] { "de", "en" })
                {
                    data.Add(scale, theme, lang);
                }
            }
        }
        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Page_names_break_only_between_words(double scale, string theme, string lang)
    {
        var before = Loc.Instance.Language;
        Loc.Instance.SetLanguage(lang);
        Design.TextScale.Apply(Avalonia.Application.Current!, scale);
        try
        {
            using var scene = await UxSettingsScenes.PageAsync("privacy", theme == "light" ? ThemeVariant.Light : ThemeVariant.Dark);
            UxStage.Settle(scene.Window);
            var names = scene.Window.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("sideItem"))
                .SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).ToList();
            Assert.Equal(8, names.Count);
            var broken = names.Select(MidWordBreak).OfType<string>().ToList();
            Assert.True(broken.Count == 0, $"{scale:P0} {theme} {lang}: " + string.Join(", ", broken));
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
            Loc.Instance.SetLanguage(before);
        }
    }

    /// <summary>„Verbindun|gen“, wenn eine Zeile zwischen zwei Buchstaben beginnt; null, wenn alle Umbrüche an Wortgrenzen liegen.</summary>
    internal static string? MidWordBreak(TextBlock block)
    {
        var text = block.Text ?? "";
        foreach (var line in block.TextLayout.TextLines.Skip(1))
        {
            var at = line.FirstTextSourceIndex;
            if (at > 0 && at < text.Length && char.IsLetterOrDigit(text[at - 1]) && char.IsLetterOrDigit(text[at]))
            {
                return text[..at] + "|" + text[at..];
            }
        }
        return null;
    }
}
