using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using WhisperLoom.App.Audio;
using WhisperLoom.App.Output;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Services;

public enum DictationState
{
    Idle,
    Recording,
    Paused,
    Processing,
}

/// <summary>
/// Der Ablauf eines Diktats in der App: Aufnahme (mit Pause) → <see cref="IDictationPipeline"/> → Ausgabe →
/// Verlauf → Log (nur Metadaten). Stufe und Sprache kommen aus den Einstellungen. Aufrufe vom UI-Thread
/// (Fenster, Tray, IPC über den Dispatcher); ein Diktat zur Zeit.
/// </summary>
public sealed partial class DictationController : ObservableObject
{
    private readonly IAudioRecorder _recorder;
    private readonly IDictationPipeline _pipeline;
    private readonly IOutputSink _output;
    private readonly IHistoryStore _history;
    private readonly AppLog _log;
    private readonly Action<WhisperLoomSettings> _saveSettings;
    private readonly Func<DateTimeOffset> _now;
    private CancellationTokenSource? _processing;
    private Task _processingTask = Task.CompletedTask;
    private DateTimeOffset _segmentStart;
    private TimeSpan _recorded;

    public DictationController(
        IAudioRecorder recorder,
        IDictationPipeline pipeline,
        IOutputSink output,
        IHistoryStore history,
        AppLog log,
        WhisperLoomSettings settings,
        Action<WhisperLoomSettings> saveSettings,
        Func<DateTimeOffset>? now = null)
    {
        _recorder = recorder;
        _pipeline = pipeline;
        _output = output;
        _history = history;
        _log = log;
        Settings = settings;
        _saveSettings = saveSettings;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Stage), nameof(Language))]
    private WhisperLoomSettings _settings = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsBusy))]
    private DictationState _state;

    [ObservableProperty]
    private string _lastRaw = "";

    [ObservableProperty]
    private string _lastResult = "";

    [ObservableProperty]
    private RefinedText? _lastRefined;

    /// <summary>Lokalisierungs-Schlüssel des letzten Fehlers (<c>Error_*</c>); null = keiner.</summary>
    [ObservableProperty]
    private string? _lastErrorKey;

    /// <summary>Klasse des letzten Fehlers für Status und Log.</summary>
    public string? LastErrorClass { get; private set; }

    public bool IsRecording => State is DictationState.Recording or DictationState.Paused;

    public bool IsBusy => State == DictationState.Processing;

    public RefineMode Stage => StageOf(Settings.Refine);

    public string Language => Settings.Language;

    public double Level => State == DictationState.Recording ? _recorder.Level : 0;

    /// <summary>Aufnahmedauer ohne Pausen.</summary>
    public TimeSpan Elapsed => State == DictationState.Recording ? _recorded + (_now() - _segmentStart) : _recorded;

    /// <summary>Gespeicherte Stufe wie <c>Prefs.refineMode</c>: Altwert „paragraphs“ und „prompt“ ohne Freischaltung → Glätten.</summary>
    public static RefineMode StageOf(RefineSettings refine)
    {
        if (refine.Stage == "paragraphs")
        {
            return RefineMode.Polish;
        }
        var mode = RefineModes.FromKey(refine.Stage);
        return mode switch
        {
            RefineMode.Prompt when !refine.PromptEnabled => RefineMode.Polish,
            RefineMode.Readable => RefineMode.Polish,
            _ => mode,
        };
    }

    /// <summary>Wirksame Verarbeitung der Stufe (<see cref="Refinement.Of"/>, Weg Diktat).</summary>
    public static Refinement RefinementOf(RefineSettings refine)
    {
        var stage = StageOf(refine);
        var paragraphs = stage switch
        {
            RefineMode.Polish => refine.ParagraphsPolish,
            RefineMode.Beautify => refine.ParagraphsBeautify,
            _ => true,
        };
        return Refinement.Of(
            RefineWay.Dictation,
            stage,
            RefineKeys.CleanupFromKey(refine.PolishCleanup),
            paragraphs,
            RefineKeys.FormFromKey(refine.SummarizeForm));
    }

    /// <returns>Kurzer Status für die CLI.</returns>
    public async Task<string> ToggleAsync()
    {
        return State switch
        {
            DictationState.Idle => Start(),
            DictationState.Recording or DictationState.Paused => await StopAsync(),
            _ => "busy",
        };
    }

    public string Start()
    {
        if (State != DictationState.Idle)
        {
            return State == DictationState.Processing ? "busy" : "already-recording";
        }
        LastErrorKey = null;
        LastErrorClass = null;
        try
        {
            _recorder.Start(StringOrNull(SettingsStore.Desktop(Settings).Microphone));
        }
        catch (AudioUnavailableException)
        {
            Fail("Error_Audio", "AudioUnavailable");
            return "audio-unavailable";
        }
        _recorded = TimeSpan.Zero;
        _segmentStart = _now();
        State = DictationState.Recording;
        _log.Event("recording.start");
        return "recording";
    }

    public string Pause()
    {
        if (State != DictationState.Recording)
        {
            return "not-recording";
        }
        _recorded += _now() - _segmentStart;
        _recorder.Pause();
        State = DictationState.Paused;
        return "paused";
    }

    public string Resume()
    {
        if (State != DictationState.Paused)
        {
            return "not-paused";
        }
        _segmentStart = _now();
        _recorder.Resume();
        State = DictationState.Recording;
        return "recording";
    }

    /// <summary>Beendet die Aufnahme und wartet, bis das Ergebnis ausgegeben ist.</summary>
    public async Task<string> StopAsync()
    {
        if (!IsRecording)
        {
            return State == DictationState.Processing ? "busy" : "not-recording";
        }
        if (State == DictationState.Recording)
        {
            _recorded += _now() - _segmentStart;
        }
        var audio = _recorder.Stop();
        State = DictationState.Processing;
        _processing = new CancellationTokenSource();
        _processingTask = ProcessAsync(audio, _processing.Token);
        await _processingTask;
        return LastErrorClass is null ? "done" : "error";
    }

    /// <summary>Verwirft die Aufnahme bzw. bricht die Verarbeitung ab.</summary>
    public string Cancel()
    {
        switch (State)
        {
            case DictationState.Recording or DictationState.Paused:
                _recorder.Cancel();
                _recorded = TimeSpan.Zero;
                State = DictationState.Idle;
                _log.Event("recording.cancel");
                return "cancelled";
            case DictationState.Processing:
                _processing?.Cancel();
                return "cancelled";
            default:
                return "idle";
        }
    }

    /// <summary><c>next</c> oder ein Stufen-Schlüssel; „prompt“ nur, wenn freigeschaltet.</summary>
    /// <exception cref="ArgumentException">Unbekannte oder nicht wählbare Stufe.</exception>
    public RefineMode SetStage(string name)
    {
        var choices = RefineModes.SettingsFor(Settings.Refine.PromptEnabled);
        RefineMode target;
        if (name == "next")
        {
            var index = choices.ToList().IndexOf(Stage);
            target = choices[(index + 1) % choices.Count];
        }
        else
        {
            target = RefineModes.FromKey(name);
            if (target.Key() != name || !choices.Contains(target))
            {
                throw new ArgumentException($"Stufe nicht wählbar: {name}", nameof(name));
            }
        }
        Settings.Refine.Stage = target.Key();
        _saveSettings(Settings);
        OnPropertyChanged(nameof(Stage));
        _log.Event("stage.set", ("stage", target.Key()));
        return target;
    }

    /// <summary>
    /// Übernimmt geänderte Einstellungen (Einstellungsfenster) und speichert sie. Schaltet die Änderung den Verlauf
    /// aus, wird er geleert (wie Android <c>History.setEnabled(false)</c>): danach liegt kein Diktattext mehr ab.
    /// </summary>
    /// <exception cref="IOException">Verlauf ließ sich nicht leeren (die Einstellung ist trotzdem übernommen).</exception>
    public async Task ApplySettingsAsync(WhisperLoomSettings settings)
    {
        var historyTurnedOff = Settings.History.Enabled && !settings.History.Enabled;
        _saveSettings(settings);
        Settings = settings;
        if (historyTurnedOff)
        {
            await _history.ClearAsync(CancellationToken.None);
            _log.Event("history.cleared", ("reason", "disabled"));
        }
    }

    private async Task ProcessAsync(AudioBuffer audio, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var refinement = RefinementOf(Settings.Refine);
        try
        {
            var result = await _pipeline.RunAsync(new DictationRequest(audio, Settings.Language, refinement), ct);
            LastRaw = result.Raw;
            LastResult = result.Text;
            LastRefined = result.Result;
            try
            {
                await _output.WriteAsync(result.Text, CancellationToken.None);
            }
            catch (IOException)
            {
                Fail("Error_Output", "OutputFailed");
            }
            await AddHistoryAsync(result);
            _log.Event(
                "dictation",
                ("status", "ok"),
                ("durationMs", result.DurationMs),
                ("processingMs", watch.ElapsedMilliseconds),
                ("stt", result.SttLabel),
                ("sttFallback", result.UsedSttFallback),
                ("stage", refinement.Mode.Key()),
                ("model", result.Result.Model),
                ("refineSkipped", result.Result.Skipped is not null),
                ("output", _output.Kind));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _log.Event("dictation", ("status", "cancelled"), ("processingMs", watch.ElapsedMilliseconds));
        }
        // IOException: die Key-Ablage ist nicht erreichbar (Ausgabe und Verlauf fangen ihre eigenen oben ab).
        catch (Exception e) when (e is ApiHttpException or ApiNetworkException or ApiNotConfiguredException or LocalNotAvailableException
                                      or IOException)
        {
            var key = e switch
            {
                ApiNotConfiguredException => "Error_NotConfigured",
                ApiHttpException => "Error_Http",
                ApiNetworkException => "Error_Network",
                IOException => "Key_StoreError",
                _ => "Error_Local",
            };
            Fail(key, e.GetType().Name);
            _log.Event(
                "dictation",
                ("status", "error"),
                ("errorClass", e.GetType().Name),
                ("httpStatus", (e as ApiHttpException)?.StatusCode),
                ("processingMs", watch.ElapsedMilliseconds));
        }
        finally
        {
            _processing?.Dispose();
            _processing = null;
            _recorded = TimeSpan.Zero;
            State = DictationState.Idle;
        }
    }

    private async Task AddHistoryAsync(DictationResult result)
    {
        if (result.Raw.Length == 0)
        {
            return;
        }
        var now = _now();
        var entry = new HistoryEntry(
            FileHistoryStore.NewId(now),
            now,
            result.Language,
            result.DurationMs,
            result.Raw,
            result.Text,
            result.Result.Refinement,
            result.SttLabel,
            result.Result.Model,
            result.Result.Skipped);
        try
        {
            await _history.AddAsync(entry, CancellationToken.None);
        }
        catch (IOException)
        {
            _log.Event("history", ("status", "error"));
        }
    }

    private void Fail(string key, string errorClass)
    {
        LastErrorKey = key;
        LastErrorClass = errorClass;
    }

    private static string? StringOrNull(string value) => value.Length == 0 ? null : value;
}
