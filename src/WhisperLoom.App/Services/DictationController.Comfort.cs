using WhisperLoom.App.Output;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Services;

/// <summary>Pipeline mit Ausweg während der Textverbesserung (<see cref="DictationPipeline.RunAsync(DictationRequest, RefineSkip?, Action?, CancellationToken)"/>).</summary>
public interface ISkippablePipeline
{
    Task<DictationResult> RunAsync(DictationRequest request, RefineSkip skip, Action? onRefineStart, CancellationToken ct);
}

/// <summary>
/// Komfort aus der Android-App (W2-OUT, Prüfrunde 1): Fehlgriffe unter 0,3 s verwerfen, Deckel bei 12 min mit Hinweis ab 10 min
/// (nur Online-Erkennung, <see cref="RecordingLimits"/>), Ausweg „sofort ohne KI“ während der Verbesserung und „Erneut senden“
/// nach einem wiederholbaren Fehler (Netz, 408, 429, 5xx) mit dem Audio aus dem Speicher – nie auf der Platte.
/// </summary>
public sealed partial class DictationController
{
    private RecordingLength _lengthNotice;
    private RefineSkip? _skip;
    private AudioBuffer? _retryAudio;

    /// <summary>Höchstlänge erreicht: die Aufnahme ist angehalten, „Weiter“ gesperrt, nur noch Senden oder Verwerfen.</summary>
    public bool Capped { get; private set; }

    /// <summary>Die Textverbesserung läuft gerade (ab hier wirkt <see cref="SkipRefine"/>).</summary>
    public bool IsRefining { get; private set; }

    /// <summary>Das letzte Diktat scheiterte wiederholbar; sein Audio liegt im Speicher für <see cref="RetryAsync"/>.</summary>
    public bool CanRetry => _retryAudio is not null && State == DictationState.Idle;

    /// <summary>Online-Erkennung (nur die hat einen Deckel).</summary>
    private bool LengthLimited => SettingsAccess.EngineOf(Settings) != Engine.Offline;

    /// <summary>
    /// Längen-Takt (≈ 10 Hz aus den Fenstern): einmal "long" ab 10 min, bei 12 min "capped" – die Aufnahme hält an wie mit Pause,
    /// „Weiter“ bleibt gesperrt. Sonst null.
    /// </summary>
    public string? CheckLength()
    {
        if (State != DictationState.Recording)
        {
            return null;
        }
        var length = RecordingLimits.Length((long)Elapsed.TotalMilliseconds, LengthLimited);
        if (length == RecordingLength.Max && !Capped)
        {
            Pause();
            Capped = true;
            _lengthNotice = length;
            _log.Event("recording.capped", ("durationMs", (long)_recorded.TotalMilliseconds));
            return "capped";
        }
        if (length == RecordingLength.Long && _lengthNotice == RecordingLength.Ok)
        {
            _lengthNotice = length;
            return "long";
        }
        return null;
    }

    /// <summary>Taste während der Verbesserung: sofort mit dem Text ohne KI weiter (ohne Hinweis, wie Android).</summary>
    /// <returns>true, wenn eine laufende Verbesserung übersprungen wird.</returns>
    public bool SkipRefine()
    {
        if (State != DictationState.Processing || _skip is null || !IsRefining)
        {
            return false;
        }
        _skip.Skip();
        _log.Event("refine.skipped");
        return true;
    }

    /// <summary>Das gemerkte Audio des gescheiterten Diktats erneut senden (Erkennung + Verbesserung + Ausgabe + Verlauf).</summary>
    /// <returns>"done", "error", "busy" oder "nothing".</returns>
    public async Task<string> RetryAsync()
    {
        if (_retryAudio is not { } audio)
        {
            return "nothing";
        }
        if (State != DictationState.Idle)
        {
            return "busy";
        }
        _retryAudio = null;
        LastErrorKey = null;
        LastErrorClass = null;
        State = DictationState.Processing;
        _processing = new CancellationTokenSource();
        _processingTask = ProcessAsync(audio, _processing.Token);
        await _processingTask;
        _log.Event("dictation.retry", ("status", LastErrorClass is null ? "ok" : "error"));
        return LastErrorClass is null ? "done" : "error";
    }

    /// <summary>Neue Aufnahme: Längen-Hinweise zurück, altes Retry-Audio verwerfen (es gehört zum alten Diktat).</summary>
    private void ResetComfort()
    {
        Capped = false;
        _lengthNotice = RecordingLength.Ok;
        _retryAudio = null;
    }

    /// <summary>
    /// Ging die Ausgabe in ein Passwortfeld (UI Automation), bleibt vom Diktat nichts: kein Verlauf, kein „erneut einfügen“
    /// (wie Android <c>HistoryPolicy</c>, W2-OUT).
    /// </summary>
    private bool ForgetIfPrivate()
    {
        if (_output is not IPrivateAwareOutput { LastWasPrivate: true })
        {
            return false;
        }
        LastRaw = "";
        LastResult = "";
        return true;
    }

    /// <summary>Kürzer als 0,3 s: Fehlgriff – nichts erkennen, nichts ausgeben, kein Verlauf.</summary>
    private bool DiscardIfTooShort(AudioBuffer audio)
    {
        if (!RecordingLimits.TooShort(audio.DurationMs))
        {
            return false;
        }
        _recorded = TimeSpan.Zero;
        ActiveProfile = null;
        State = DictationState.Idle;
        _log.Event("recording.too-short", ("durationMs", audio.DurationMs));
        return true;
    }

    /// <summary>Pipeline mit Ausweg, wenn sie ihn kann; sonst wie bisher.</summary>
    private async Task<DictationResult> RunPipelineAsync(DictationRequest request, CancellationToken ct)
    {
        if (_pipeline is not ISkippablePipeline skippable)
        {
            return await _pipeline.RunAsync(request, ct);
        }
        _skip = new RefineSkip();
        try
        {
            return await skippable.RunAsync(request, _skip, () => IsRefining = true, ct);
        }
        finally
        {
            _skip = null;
            IsRefining = false;
        }
    }

    /// <summary>Wiederholbarer Fehler (Android <c>ApiErrors.kt:104-108</c>): Audio für „Erneut senden“ im Speicher behalten.</summary>
    private void KeepForRetry(Exception e, AudioBuffer audio)
    {
        _retryAudio = e.IsRetryable() ? audio : null;
    }
}
