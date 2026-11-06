using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using WhisperLoom.App.Features.Selection;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Größe des schwebenden Knopfs (TP-WBUTTON): kompakt neben der Markierung, Bedienfläche aber mindestens 24 × 24 DIP
/// (WCAG 2.5.8 Zielgröße). Maße bei 100 % Textgröße, mit und ohne Tastenkappe.
/// </summary>
public sealed class SelectionButtonSizeTest
{
    /// <summary>Höchstens so hoch (DIP); vorher 32 (MinHeight) und Symbol 20.</summary>
    private const double MaxHeight = 28;

    [AvaloniaTheory]
    [InlineData(null)]
    [InlineData("Ctrl+Alt+M")]
    public void Knopf_kompakt_aber_mindestens_24_mal_24(string? shortcut)
    {
        var window = new SelectionButtonWindow();
        window.SetShortcut(shortcut);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = window.Button.Bounds;
        var whole = window.Bounds;
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"Kappe={shortcut ?? "-"}: Knopf {button.Width:F0} × {button.Height:F0} DIP, Fenster {whole.Width:F0} × {whole.Height:F0} DIP");
        window.Close();

        Assert.True(button.Width >= 24 && button.Height >= 24, $"Bedienfläche {button.Width} × {button.Height} < 24 × 24");
        Assert.True(button.Height <= MaxHeight, $"Knopf {button.Height} DIP hoch, erlaubt ≤ {MaxHeight}");
    }
}
