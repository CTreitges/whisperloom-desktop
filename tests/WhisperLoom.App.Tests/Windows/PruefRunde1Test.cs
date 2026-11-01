using WhisperLoom.App.Platform.Windows.Input;
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

    // --- B1f Leerzeichen davor (UIA TextPattern) ------------------------------------------------

    private sealed class FakeCaret(Func<char?> before) : ICaretSource
    {
        public int Calls { get; private set; }

        public char? CharBeforeCaret()
        {
            Calls++;
            return before();
        }
    }

    private Task<InsertOutcome> InsertWithCaret(ICaretSource caret, bool leadingSpace = true, FocusKind focus = FocusKind.Editable)
    {
        _rig.Focus.Kind = focus;
        var inserter = new TextInserter(_rig.Clipboard, _rig.Keys, _rig.Windows, TimeSpan.Zero, _rig.Focus, _ => Task.CompletedTask,
            new CaretContext(caret));
        return inserter.InsertAsync(42, "Hallo", InsertMode.Paste, false, CancellationToken.None, leadingSpace: leadingSpace);
    }

    [Fact]
    public async Task B1f_direkt_an_ein_Wort_bekommt_ein_Leerzeichen_davor()
    {
        await InsertWithCaret(new FakeCaret(() => 't'));
        Assert.Equal(" Hallo", _rig.Keys.Target);
    }

    [Fact]
    public async Task B1f_nach_Leerraum_oder_ohne_Auskunft_kein_Leerzeichen()
    {
        await InsertWithCaret(new FakeCaret(() => ' '));
        await InsertWithCaret(new FakeCaret(() => null));
        await InsertWithCaret(new FakeCaret(() => throw new System.Runtime.InteropServices.COMException()));
        Assert.Equal("HalloHalloHallo", _rig.Keys.Target);
    }

    [Fact]
    public async Task B1f_nur_beim_Diktat_und_nie_im_Passwortfeld_gefragt()
    {
        var caret = new FakeCaret(() => 't');
        await InsertWithCaret(caret, leadingSpace: false);
        await InsertWithCaret(caret, focus: FocusKind.Password);
        Assert.Equal(0, caret.Calls);
        Assert.Equal("Hallo", _rig.Keys.Target);
    }

    [Fact]
    public void B1f_echte_UIA_Abfrage_wirft_nicht()
    {
        // Nur lesen (1 Zeichen vor der Schreibmarke des gerade fokussierten Elements): prüft die COM-Deklaration.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("nur Windows");
            return;
        }
        _ = new UiaCaretSource().CharBeforeCaret();
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
