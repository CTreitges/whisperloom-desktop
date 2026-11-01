using Avalonia.Headless.XUnit;
using WhisperLoom.App.Output;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// „Kopieren funktioniert“ (W2-OUT): Wiederholung bei belegter Zwischenablage mit Gegenprobe, Tray-Klick lässt den Text in der
/// Zwischenablage, Hinweis ohne Einfügen, Passwortfeld ohne Verlauf.
/// </summary>
public sealed class CopyOutputTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly CaptureSink _inner = new();
    private readonly InsertOutputSink _sink;
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;

    public CopyOutputTest()
    {
        _sink = new InsertOutputSink(_inner, _rig.Inserter, () => InsertMode.Paste, () => _f!.Controller.Settings.Output);
        _f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" }, Polish = new PolishSettings { TrailingSpace = false } }, _sink);
        _f.Pipeline.RawText = "Hallo Welt";
        _flow = new InputFlow(_f.Controller, _sink, new FakePresenter(), () => _rig.Windows.Current, _ => { });
    }

    public void Dispose() => _f.Dispose();

    // --- ClipboardCopier ----------------------------------------------------------------------

    private static (ClipboardCopier Copier, Func<int> Calls) Flaky(int busy, Func<string?>? read = null)
    {
        var calls = 0;
        string? stored = null;
        var copier = new ClipboardCopier(
            t =>
            {
                calls++;
                if (calls <= busy)
                {
                    throw new IOException("belegt");
                }
                stored = t;
                return Task.CompletedTask;
            },
            () => Task.FromResult(read is null ? stored : read()),
            _ => Task.CompletedTask);
        return (copier, () => calls);
    }

    [Fact]
    public async Task Kopieren_haelt_eine_kurz_belegte_Zwischenablage_aus()
    {
        // Ein Zwischenablage-Manager hält sie für mehrere Versuche (früher: ein Versuch mit 10×50 ms Öffnen, dann Fehler).
        var (copier, calls) = Flaky(busy: 4);
        Assert.True(await copier.CopyAsync("Diktat"));
        Assert.Equal(5, calls());
    }

    [Fact]
    public async Task Kopiert_erst_nach_bestandener_Gegenprobe()
    {
        var reads = 0;
        // Erste Gegenprobe: ein anderes Programm hat sofort überschrieben – erneut schreiben.
        var (copier, calls) = Flaky(busy: 0, read: () => ++reads == 1 ? "fremd" : "Diktat");
        Assert.True(await copier.CopyAsync("Diktat"));
        Assert.Equal(2, calls());
    }

    [Fact]
    public async Task Dauerhaft_belegt_meldet_Fehlschlag_statt_Kopiert()
    {
        var (copier, calls) = Flaky(busy: int.MaxValue);
        Assert.False(await copier.CopyAsync("Diktat"));
        Assert.Equal(ClipboardCopier.Backoff.Count + 1, calls());
    }

    // --- Tray-Klick (Regression: Text ging verloren) -----------------------------------------

    [AvaloniaFact]
    public async Task Tray_klick_ohne_Eingabefeld_legt_den_Text_in_die_Zwischenablage()
    {
        _rig.Focus.Kind = FocusKind.NotEditable;
        var result = await _flow.InsertRecentAsync("Aus dem Verlauf");
        Assert.Equal("Aus dem Verlauf", _rig.Clipboard.Text);
        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal(nameof(InsertOutcome.TargetGone), result); // noch kein Diktat: kein Ziel – trotzdem in der Zwischenablage
    }

    [AvaloniaFact]
    public async Task Tray_klick_fuegt_ein_und_der_Text_bleibt_in_der_Zwischenablage()
    {
        // Erst ein Diktat per Kürzel, damit es ein Ziel gibt.
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        var before = _rig.Keys.Target;
        _rig.Focus.Kind = FocusKind.Unknown;
        Assert.Equal(nameof(InsertOutcome.Pasted), await _flow.InsertRecentAsync("Nochmal"));
        Assert.Equal(before + "Nochmal", _rig.Keys.Target);
        Assert.Equal("Nochmal", _rig.Clipboard.Text);

        // Auch in ein sicher erkanntes Eingabefeld: der Tray-Klick ist zugleich „Kopieren“.
        _rig.Focus.Kind = FocusKind.Editable;
        await _flow.InsertRecentAsync("Drittens");
        Assert.Equal("Drittens", _rig.Clipboard.Text);
    }

    [AvaloniaFact]
    public async Task Tray_klick_ohne_Eingabefeld_nach_einem_Diktat()
    {
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        var before = _rig.Keys.Target;
        _rig.Focus.Kind = FocusKind.NotEditable;
        Assert.Equal(nameof(InsertOutcome.NoTextField), await _flow.InsertRecentAsync("Aus dem Verlauf"));
        Assert.Equal(before, _rig.Keys.Target);
        Assert.Equal("Aus dem Verlauf", _rig.Clipboard.Text);
    }

    // --- Passwortfeld: kein Verlauf ---------------------------------------------------------

    [AvaloniaFact]
    public async Task Diktat_ins_Passwortfeld_landet_nirgends()
    {
        _rig.Focus.Kind = FocusKind.Password;
        var hints = new List<InsertOutcome>();
        _flow.FallbackToast += hints.Add;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        Assert.Equal("", _rig.Keys.Target);
        Assert.Equal("alt", _rig.Clipboard.Text);
        Assert.Empty(await _f.History.ListAsync(CancellationToken.None));
        Assert.Equal("", _f.Controller.LastResult);
        Assert.Equal([InsertOutcome.PasswordField], hints);
        Assert.DoesNotContain("Hallo Welt", _f.LogText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Diktat_ohne_Eingabefeld_landet_in_Zwischenablage_und_Verlauf()
    {
        _rig.Focus.Kind = FocusKind.NotEditable;
        await _flow.HandleAsync(HotkeyAction.Toggle);
        await _flow.HandleAsync(HotkeyAction.OverlaySend);
        Assert.Equal(_f.Controller.LastResult, _rig.Clipboard.Text);
        Assert.Single(await _f.History.ListAsync(CancellationToken.None));
    }

    // --- Hinweis ohne Einfügen (Linux, Tray/CLI) --------------------------------------------

    [Fact]
    public async Task Ohne_Einfuegen_Zwischenablage_und_Hinweis_Kopiert()
    {
        var shown = new List<OutputHint>();
        var settings = new OutputSettings();
        var sink = new HintingOutputSink(_inner, () => settings, shown.Add);
        await sink.WriteAsync("Linux-Diktat", CancellationToken.None);
        Assert.Equal(["Linux-Diktat"], _inner.Written);
        Assert.Equal([OutputHint.Copied], shown);
        Assert.Equal(new OutputPlan(OutputAction.ClipboardWithHint, true, OutputHint.Copied, true), sink.LastPlan);
    }

    [Fact]
    public async Task Fehlt_notify_send_bleibt_es_still()
    {
        Assert.False(await DesktopNotify.ShowAsync("x", new MissingRunner()));
    }

    private sealed class MissingRunner : Secrets.IProcessRunner
    {
        public Task<Secrets.ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct) =>
            throw new System.ComponentModel.Win32Exception(2);

        public Task<int> RunUntilExitAsync(string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken ct) =>
            throw new System.ComponentModel.Win32Exception(2);
    }
}
