using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Fokus-Prüfung (UIA, Fake-Quelle) und die Ausgabe-Entscheidung im Einfüge-Ablauf (W2-OUT).</summary>
public class FocusDecisionTest
{
    private sealed class FakeSource(Func<FocusInfo?> focused) : IFocusSource
    {
        public FocusInfo? Focused() => focused();
    }

    // --- Klassifizierung ---------------------------------------------------------------------

    [Theory]
    [InlineData(50004, false, false, false, FocusKind.Editable)] // Edit ohne ValuePattern (Win32-Edit meldet es aber meist)
    [InlineData(50004, false, true, false, FocusKind.Editable)]
    [InlineData(50004, false, true, true, FocusKind.NotEditable)] // schreibgeschütztes Feld
    [InlineData(50004, true, true, false, FocusKind.Password)]
    [InlineData(50030, false, true, false, FocusKind.Editable)] // Dokument mit Wert (Notepad, RichEdit)
    [InlineData(50030, false, false, false, FocusKind.Unknown)] // Webseite ohne Eingabefeld meldet Document
    [InlineData(50000, false, false, false, FocusKind.NotEditable)] // Button
    [InlineData(50007, false, false, false, FocusKind.NotEditable)] // ListItem
    [InlineData(50003, false, true, false, FocusKind.Editable)] // ComboBox mit editierbarem Wert
    [InlineData(50020, false, false, false, FocusKind.Unknown)] // Text (Terminals)
    [InlineData(50033, false, false, false, FocusKind.Unknown)] // Pane
    [InlineData(50025, false, false, false, FocusKind.Unknown)] // Custom
    public void Klassifiziert(int controlType, bool password, bool value, bool readOnly, FocusKind expected) =>
        Assert.Equal(expected, FocusProbe.Classify(new FocusInfo(controlType, password, value, readOnly)));

    [Fact]
    public void OhneElementUnbekannt() => Assert.Equal(FocusKind.Unknown, FocusProbe.Classify(null));

    [Fact]
    public async Task ProbeLiefertDieKlasseDerQuelle()
    {
        var probe = new FocusProbe(new FakeSource(() => new FocusInfo(50004, true, true, false)));
        Assert.Equal(FocusKind.Password, await probe.ProbeAsync(CancellationToken.None));
        Assert.Equal((FocusKind.Password, 50004), probe.Last);
    }

    [Fact]
    public async Task ComFehlerGiltAlsUnbekannt()
    {
        var probe = new FocusProbe(new FakeSource(() => throw new System.Runtime.InteropServices.COMException("UIA")));
        Assert.Equal(FocusKind.Unknown, await probe.ProbeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task HaengendeAppHaeltDasEinfuegenNichtAuf()
    {
        using var release = new ManualResetEventSlim();
        var probe = new FocusProbe(new FakeSource(() =>
        {
            release.Wait(TimeSpan.FromSeconds(5));
            return new FocusInfo(50004, false, true, false);
        }), TimeSpan.FromMilliseconds(50));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(FocusKind.Unknown, await probe.ProbeAsync(CancellationToken.None));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        release.Set();
    }

    [Fact]
    public void EchteUiaAbfrageLiefertEinGueltigesErgebnis()
    {
        // Nur lesen, keine Eingabe: prüft die Vtable-Reihenfolge der COM-Deklaration am echten UIAutomationCore.
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("nur Windows");
            return;
        }
        var info = new UiaFocusSource().Focused();
        if (info is not null)
        {
            Assert.InRange(info.ControlType, 50000, 50045);
        }
    }

    // --- Einfüge-Ablauf mit Entscheidung -----------------------------------------------------

    private readonly InsertRig _rig = new();

    private Task<InsertOutcome> Insert(bool alwaysCopy = false, InsertMode mode = InsertMode.Paste) =>
        _rig.Inserter.InsertAsync(42, "Hallo Welt", mode, pressEnter: true, CancellationToken.None, alwaysCopy);

    [Fact]
    public async Task PasswortfeldWederEinfuegenNochKopierenNochEnter()
    {
        _rig.Focus.Kind = FocusKind.Password;
        Assert.Equal(InsertOutcome.PasswordField, await Insert(alwaysCopy: true));
        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Equal(0, _rig.Keys.Enters);
        Assert.DoesNotContain("set", _rig.Steps);
        Assert.Equal(OutputHint.PasswordField, InsertOutcome.PasswordField.Hint());
    }

    [Fact]
    public async Task OhneEingabefeldInDieZwischenablageMitHinweis()
    {
        _rig.Focus.Kind = FocusKind.NotEditable;
        Assert.Equal(InsertOutcome.NoTextField, await Insert());
        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
        Assert.False(_rig.Clipboard.Private);
        Assert.DoesNotContain("ctrl-v", _rig.Steps);
        Assert.Equal(0, _rig.Keys.Enters);
        Assert.Equal(OutputHint.Copied, InsertOutcome.NoTextField.Hint());
    }

    [Fact]
    public async Task EingabefeldEinfuegenUndAlteZwischenablageZurueck()
    {
        Assert.Equal(InsertOutcome.Pasted, await Insert());
        Assert.Equal("Hallo Welt", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Equal(OutputHint.None, InsertOutcome.Pasted.Hint());
    }

    [Fact]
    public async Task ImmerKopierenLaesstDenTextNachDemEinfuegenInDerZwischenablage()
    {
        Assert.Equal(InsertOutcome.Pasted, await Insert(alwaysCopy: true));
        Assert.Equal("Hallo Welt", _rig.Keys.Target);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
        Assert.False(_rig.Clipboard.Private);
        Assert.DoesNotContain("restore", _rig.Steps);
    }

    [Fact]
    public async Task UnbekannterFokusFuegtEinUndBehaeltDieKopie()
    {
        _rig.Focus.Kind = FocusKind.Unknown;
        Assert.Equal(InsertOutcome.Pasted, await Insert());
        Assert.Equal("Hallo Welt", _rig.Keys.Target);
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task TippenMitImmerKopieren()
    {
        Assert.Equal(InsertOutcome.Typed, await Insert(alwaysCopy: true, mode: InsertMode.Type));
        Assert.Equal("Hallo Welt", _rig.Clipboard.Text);
    }

    [Fact]
    public async Task BelegteZwischenablageBeimFallbackWirdWiederholt()
    {
        _rig.Focus.Kind = FocusKind.NotEditable;
        var busy = new BusyClipboard(_rig.Clipboard, failures: 3);
        var inserter = new TextInserter(busy, _rig.Keys, _rig.Windows, TimeSpan.Zero, _rig.Focus, _ => Task.CompletedTask);
        Assert.Equal(InsertOutcome.NoTextField, await inserter.InsertAsync(42, "Hallo", InsertMode.Paste, false, CancellationToken.None));
        Assert.Equal("Hallo", _rig.Clipboard.Text);
        Assert.Equal(4, busy.Calls);
    }

    // --- Enter: Erben / An / Aus -------------------------------------------------------------

    [Theory]
    [InlineData(null, false, false)] // Erben, global aus
    [InlineData(null, true, true)] // Erben, global an
    [InlineData(true, false, true)] // Profil An schlägt global aus
    [InlineData(false, true, false)] // Profil Aus schlägt global an
    public async Task EnterFolgtProfilSonstEinstellung(bool? profile, bool global, bool expected)
    {
        Assert.Equal(expected, InsertOutputSink.EffectivePressEnter(profile, new OutputSettings { PressEnter = global }));
        var sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste, () => new OutputSettings { PressEnter = global });
        sink.Arm(42, profile);
        await sink.WriteAsync("x", CancellationToken.None);
        Assert.Equal(expected ? 1 : 0, _rig.Keys.Enters);
    }

    [Fact]
    public async Task SenkeMeldetPasswortfeldAlsPrivat()
    {
        _rig.Focus.Kind = FocusKind.Password;
        var sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste);
        sink.Arm(42);
        await sink.WriteAsync("geheim", CancellationToken.None);
        Assert.True(sink.LastWasPrivate);
        await sink.WriteAsync("cli", CancellationToken.None);
        Assert.False(sink.LastWasPrivate);
    }

    /// <summary>Zwischenablage, die die ersten <paramref name="failures"/> öffentlichen Schreibversuche als belegt abweist.</summary>
    private sealed class BusyClipboard(FakeClipboard inner, int failures) : IClipboardAdapter
    {
        public int Calls { get; private set; }

        public uint SequenceNumber => inner.SequenceNumber;

        public ClipboardSnapshot Save() => inner.Save();

        public void SetText(string text, bool @private)
        {
            Calls++;
            if (Calls <= failures)
            {
                throw new IOException("belegt");
            }
            inner.SetText(text, @private);
        }

        public void Restore(ClipboardSnapshot snapshot) => inner.Restore(snapshot);

        public string? GetText() => inner.GetText();
    }
}
