using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.ViewModels;

namespace WhisperLoom.App.Setup;

/// <summary>
/// Pegel-Test im Assistenten: nimmt mit dem gewählten Mikrofon auf und zeigt nur den Pegel. Die Aufnahme bleibt im
/// Speicher und wird beim Stoppen verworfen (<see cref="IAudioRecorder.Cancel"/>), nichts geht an eine Erkennung.
/// <see cref="Tick"/> ruft der Fenster-Timer; Tests rufen es direkt.
/// </summary>
public sealed partial class MicLevelTest(IAudioRecorder recorder, Func<string> microphone) : ObservableObject
{
    private readonly IAudioRecorder _recorder = recorder;
    private readonly Func<string> _microphone = microphone;

    [ObservableProperty]
    private double _level;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand), nameof(StopCommand))]
    private bool _running;

    /// <summary>Hinweis: „sprich etwas“, „Signal erkannt“ oder ein Fehler.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>Höchster Pegel seit dem Start; ab <see cref="HeardThreshold"/> gilt das Mikrofon als funktionierend.</summary>
    public double Peak { get; private set; }

    public const double HeardThreshold = 0.05;

    [RelayCommand(CanExecute = nameof(CanStart))]
    public void Start()
    {
        Peak = 0;
        try
        {
            var mic = _microphone();
            _recorder.Start(mic.Length == 0 ? null : mic);
        }
        catch (AudioUnavailableException)
        {
            Status = Loc.Instance["Setup_MicError"];
            return;
        }
        Running = true;
        Status = Loc.Instance["Setup_MicSpeak"];
    }

    /// <summary>Beendet den Test und verwirft die Aufnahme.</summary>
    [RelayCommand(CanExecute = nameof(Running))]
    public void Stop()
    {
        if (!Running)
        {
            return;
        }
        _recorder.Cancel();
        Running = false;
        Level = 0;
    }

    public void Tick()
    {
        if (!Running)
        {
            return;
        }
        Level = Math.Clamp(_recorder.Level, 0, 1);
        Peak = Math.Max(Peak, Level);
        Status = Loc.Instance[Peak >= HeardThreshold ? "Setup_MicHeard" : "Setup_MicSpeak"];
    }

    private bool CanStart() => !Running;
}

/// <summary>
/// Hotkey-Test (Windows): wartet über den globalen Hook auf die nächste Kombi und meldet, ob sie Halten oder Umschalten
/// entspricht (bzw. Stufe, erneut einfügen, umschreiben). Ohne laufenden Hook (Testbetrieb, Hook nicht installierbar) steht nur ein Hinweis da.
/// </summary>
public sealed partial class HotkeyCheck(HotkeysViewModel hotkeys, Func<Task<string?>>? record) : ObservableObject
{
    private readonly HotkeysViewModel _hotkeys = hotkeys;
    private readonly Func<Task<string?>>? _record = record;

    public bool Available => _record is not null;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(TestCommand))]
    private bool _waiting;

    [ObservableProperty]
    private string _status = "";

    /// <summary>true, sobald eine konfigurierte Kombi erkannt wurde.</summary>
    [ObservableProperty]
    private bool _recognized;

    [RelayCommand(CanExecute = nameof(CanTest))]
    public async Task Test()
    {
        if (_record is null)
        {
            return;
        }
        Waiting = true;
        Recognized = false;
        Status = Loc.Instance["Setup_HotkeyWaiting"];
        try
        {
            var pressed = Hotkey.Parse(await _record());
            if (pressed is null)
            {
                Status = Loc.Instance["Setup_HotkeyNone"];
                return;
            }
            var match = new[] { _hotkeys.Hold, _hotkeys.Toggle, _hotkeys.Stage, _hotkeys.Reinsert, _hotkeys.Rewrite }.FirstOrDefault(r => Hotkey.Parse(r.Value) == pressed);
            Recognized = match is not null;
            Status = match is null
                ? Loc.Instance.Format("Setup_HotkeyOther", pressed.ToString())
                : Loc.Instance.Format("Setup_HotkeyOk", match.Label, match.Value);
        }
        finally
        {
            Waiting = false;
        }
    }

    private bool CanTest() => Available && !Waiting;
}
