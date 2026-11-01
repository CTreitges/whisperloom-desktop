using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Nacharbeit Prüfrunde 1 W2-OUT (review-orchestrator.md): B2 „nur Zwischenablage“ + Passwortfeld, B3 WebView2.</summary>
public class PruefRunde1Test
{
    private readonly InsertRig _rig = new();

    [Fact]
    public async Task B2_nur_Zwischenablage_kopiert_nie_aus_einem_Passwortfeld()
    {
        _rig.Focus.Kind = FocusKind.Password;
        Assert.Equal(InsertOutcome.PasswordField,
            await _rig.Inserter.InsertAsync(42, "geheim", InsertMode.ClipboardOnly, false, CancellationToken.None));
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.DoesNotContain("set", _rig.Steps);
    }

    [Fact]
    public async Task B2_nur_Zwischenablage_kopiert_sonst_wie_bisher_ohne_das_Ziel_zu_aktivieren()
    {
        _rig.Focus.Kind = FocusKind.Editable;
        Assert.Equal(InsertOutcome.ClipboardOnly,
            await _rig.Inserter.InsertAsync(42, "Text", InsertMode.ClipboardOnly, false, CancellationToken.None));
        Assert.Equal("Text", _rig.Clipboard.Text);
        Assert.DoesNotContain("activate", _rig.Steps);
    }

    [Fact]
    public async Task B2_Ziel_nicht_vorn_dann_ohne_Fokusabfrage_kopieren()
    {
        _rig.Focus.Kind = FocusKind.Password;
        _rig.Windows.Alive.Add(7);
        _rig.Windows.Current = 7; // Nutzer ist weitergegangen: der Fokus von 42 ist nicht abfragbar
        Assert.Equal(InsertOutcome.ClipboardOnly,
            await _rig.Inserter.InsertAsync(42, "Text", InsertMode.ClipboardOnly, false, CancellationToken.None));
        Assert.Equal(0, _rig.Focus.Probes);
    }

    [Fact]
    public async Task B3_WebView2_App_gilt_als_Browser()
    {
        _rig.Focus.Kind = FocusKind.Unknown;
        _rig.Windows.Processes[42] = "msedgewebview2.exe";
        Assert.Equal(InsertOutcome.Pasted, await _rig.Inserter.InsertAsync(42, "Hallo", InsertMode.Paste, false, CancellationToken.None));
        Assert.Equal("alt", _rig.Clipboard.Text);
    }
}
