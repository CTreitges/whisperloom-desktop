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
    private AudioBuffer? _retryAudio;

    /// <summary>Höchstlänge erreicht: die Aufnahme ist angehalten, „Weiter“ gesperrt, nur noch Senden oder Verwerfen.</summary>
    public bool Capped { get; private set; }

    /// <summary>
    /// Eine Textverbesserung läuft gerade (ab hier wirkt <see cref="SkipRefine"/>), auch im Hintergrund während der nächsten
    /// Aufnahme (W3-NEXT). Aus jedem Thread lesbar (Escape im Tastatur-Hook).
    /// </summary>
    public bool IsRefining => Volatile.Read(ref _refining) > 0;

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

    /// <summary>
    /// Escape während der Verbesserung (W3-NEXT; früher die Diktat-Taste, wie Android): laufende Verbesserungen enden sofort mit
    /// dem Text ohne KI (ohne Hinweis). Eine gleichzeitig laufende Aufnahme läuft weiter.
    /// </summary>
    /// <returns>true, wenn eine laufende Verbesserung übersprungen wird.</returns>
    public bool SkipRefine()
    {
        var refining = _jobs.Where(j => j.Refining).ToList();
        if (refining.Count == 0)
        {
            return false;
        }
        foreach (var job in refining)
        {
            job.Skip.Skip();
        }
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
        var job = StartJob(audio);
        await job.Done;
        _log.Event("dictation.retry", ("status", job.ErrorClass is null ? "ok" : "error"));
        return job.ErrorClass is null ? "done" : "error";
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
    private bool ForgetIfPrivate(IOutputSink output)
    {
        if (output is not IPrivateAwareOutput { LastWasPrivate: true })
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
        State = Settled;
        _log.Event("recording.too-short", ("durationMs", audio.DurationMs));
        return true;
    }

    /// <summary>Pipeline mit Ausweg, wenn sie ihn kann; sonst wie bisher.</summary>
    private async Task<DictationResult> RunPipelineAsync(DictationRequest request, Job job)
    {
        if (_pipeline is not ISkippablePipeline skippable)
        {
            return await _pipeline.RunAsync(request, job.Cts.Token);
        }
        try
        {
            // Aus dem Thread der Pipeline: nur Zähler und Merker, kein PropertyChanged (Tray und Fenster hängen daran).
            return await skippable.RunAsync(request, job.Skip, () =>
            {
                job.Refining = true;
                Interlocked.Increment(ref _refining);
            }, job.Cts.Token);
        }
        finally
        {
            if (job.Refining)
            {
                job.Refining = false;
                Interlocked.Decrement(ref _refining);
            }
        }
    }

    /// <summary>Wiederholbarer Fehler (Android <c>ApiErrors.kt:104-108</c>): Audio für „Erneut senden“ im Speicher behalten.</summary>
    /// <remarks>
    /// W3-NEXT: nur das zuletzt gestartete Diktat ohne neue Aufnahme danach – sonst schickte „Erneut senden“ das Audio eines
    /// älteren Diktats ins Ziel des neueren bzw. löschte ein älterer Fehler das Audio des neueren.
    /// </remarks>
    private void KeepForRetry(Exception e, AudioBuffer audio, Job job)
    {
        if (job != _latestJob || IsRecording)
        {
            return;
        }
        _retryAudio = e.IsRetryable() ? audio : null;
    }
}
