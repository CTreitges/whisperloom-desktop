using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Profiles;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>
/// W2-FIX M-4/N-5/V-3 (Security-Review H8): „Markierung ersetzen“ holt das Ziel nie zurück. Ersetzt wird nur, wenn dasselbe
/// Fenster vorn ist und dasselbe Element (UIA-RuntimeId) den Fokus hat; sonst Zwischenablage + Hinweis. Passwortfeld: die
/// Markierung wird nicht gelesen, auch wenn die Fokus-Prüfung „unbekannt“ meldet.
/// </summary>
public sealed class SelectionReplaceGuardTest : IDisposable
{
    private readonly InsertRig _rig = new();
    private readonly FakePresenter _ui = new();
    private readonly FakeSelectionPresenter _picker = new();
    private readonly FakeSelectionSource _uia = new();
    private readonly ControllerFixture _f;
    private readonly InputFlow _flow;
    private readonly InsertOutputSink _sink;

    public SelectionReplaceGuardTest()
    {
        var settings = new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } };
        _sink = new InsertOutputSink(new CaptureSink(), _rig.Inserter, () => InsertMode.Paste);
        _f = new ControllerFixture(settings, _sink);
        _rig.Windows.Alive.Add(7);
        _flow = new InputFlow(_f.Controller, _sink, _ui, () => _rig.Windows.Current, _ => { }, _ => new WindowInfo("Teams", "Chat"))
        {
            Selection = SelectionTesting.Wiring(_rig, _uia, _picker),
        };
    }

    public void Dispose() => _f.Dispose();

    private async Task ChooseAsync()
    {
        _flow.PickerKey(new Hotkey(HotkeyModifiers.None, Hotkey.VkReturn));
        for (var i = 0; i < 200 && (_f.Controller.State != DictationState.Idle || _flow.Picker is not null || _sink.LastOutcome is null); i++)
        {
            await Task.Delay(10);
        }
    }

    [AvaloniaFact]
    public async Task Gleiches_Fenster_gleiches_Element_ersetzt_ohne_Aktivieren()
    {
        _uia.Next = new UiaSelection(true, "an Person A");
        await _flow.HandleAsync(HotkeyAction.Selection);
        Assert.NotNull(_picker.Picker);

        await ChooseAsync();

        Assert.Equal(InsertOutcome.Pasted, _sink.LastOutcome);
        Assert.Equal("[polish] an Person A", _rig.Keys.Target);
        Assert.DoesNotContain("activate", _rig.Steps);
    }

    [AvaloniaFact]
    public async Task Anderer_Chat_im_selben_Fenster_Zwischenablage_statt_Einfuegen()
    {
        _uia.Next = new UiaSelection(true, "an Person A");
        await _flow.HandleAsync(HotkeyAction.Selection);
        _rig.Focus.Element = "el-person-b"; // Nutzer wechselt im selben Fenster zum Chat mit Person B

        await ChooseAsync();

        Assert.Equal(InsertOutcome.NotForeground, _sink.LastOutcome);
        Assert.Equal("", _rig.Keys.Target);
        Assert.DoesNotContain("ctrl-v", _rig.Steps);
        Assert.Equal("[polish] an Person A", _rig.Clipboard.Text);
        Assert.Equal(OutputHint.Copied, InsertOutcome.NotForeground.Hint());
    }

    [AvaloniaFact]
    public async Task Anderes_Programm_vorn_wird_nicht_zurueckgeholt()
    {
        _uia.Next = new UiaSelection(true, "vertraulich");
        await _flow.HandleAsync(HotkeyAction.Selection);
        _rig.Windows.Current = 7;

        await ChooseAsync();

        Assert.Equal(InsertOutcome.NotForeground, _sink.LastOutcome);
        Assert.DoesNotContain("activate", _rig.Steps);
        Assert.DoesNotContain("ctrl-v", _rig.Steps);
        Assert.Equal(7, _rig.Windows.Current);
    }

    [AvaloniaFact]
    public async Task V3_Passwortfeld_laut_UIA_bei_unbekanntem_Fokus_wird_nicht_gelesen()
    {
        _rig.Focus.Kind = FocusKind.Unknown;
        _uia.Next = new UiaSelection(false, "", IsPassword: true);
        _rig.Keys.Selection = "geheim";

        Assert.Equal("password", await _flow.StartSelectionAsync(SelectionTrigger.Hotkey, 42, null));

        Assert.Null(_picker.Picker);
        Assert.DoesNotContain("ctrl-c", _rig.Steps);
        Assert.Empty(_ui.Hints);
    }

    [AvaloniaFact]
    public async Task Erfassung_merkt_das_fokussierte_Element()
    {
        _rig.Focus.Element = "el-7";
        _rig.Keys.Selection = "per Strg+C";
        var reader = SelectionTesting.Wiring(_rig, _uia, _picker).Reader;

        var read = await reader.ReadAsync(42, console: false, CancellationToken.None);

        Assert.Equal((SelectionReadKind.Text, "el-7"), (read.Kind, read.ElementId));
    }
}
