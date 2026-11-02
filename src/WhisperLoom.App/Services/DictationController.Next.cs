using WhisperLoom.App.Output;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Services;

/// <summary>Ausgabe, die ihr gemerktes Ziel an ein Diktat binden kann (W3-NEXT, Windows-Einfügen).</summary>
public interface IBindableOutput
{
    /// <summary>Senke mit dem jetzt gemerkten Ziel für genau ein Diktat; die Ausgabe selbst ist danach wieder frei.</summary>
    IOutputSink Bind();
}

/// <summary>
/// Nächstes Diktat während der Verarbeitung (W3-NEXT, bewusste Abweichung von Android, <c>docs/parity-exceptions.md</c>):
/// Ein Start (Kürzel, Tray, IPC <c>toggle</c>), während noch ein Diktat verarbeitet wird, beginnt sofort die nächste Aufnahme.
/// Die laufenden Diktate laufen im Hintergrund zu Ende; ihre Ausgaben kommen streng in Diktat-Reihenfolge. Ein zweiter Druck
/// innerhalb von <see cref="DoublePressWindow"/> nach dem Druck, der die neue Aufnahme gestartet hat, verwirft diese Aufnahme
/// und überspringt die laufende Verbesserung (Rohtext). OS-neutral: Windows-Kürzel und Linux-<c>toggle</c> landen hier.
/// </summary>
public sealed partial class DictationController
{
    /// <summary>Doppeldruck: zweiter Druck so kurz nach dem Start der nächsten Aufnahme = überspringen statt aufnehmen.</summary>
    public static readonly TimeSpan DoublePressWindow = TimeSpan.FromMilliseconds(400);

    private readonly List<Job> _jobs = [];
    private Task _outputTurn = Task.CompletedTask;
    private DateTimeOffset? _nextPressAt;
    private int _refining;

    /// <summary>Diktate in Verarbeitung, deren Ausgabe noch aussteht (auch während schon die nächste Aufnahme läuft).</summary>
    public int Pending => _jobs.Count;

    /// <summary>Es wird verarbeitet, und eine neue Aufnahme darf trotzdem starten (nur Diktate, nicht Umschreiben/Neu verarbeiten).</summary>
    public bool CanStartNext => State == DictationState.Processing && _jobs.Count > 0;

    /// <summary>Zustand ohne Aufnahme: verarbeitet, solange Diktate ausstehen, sonst bereit.</summary>
    private DictationState Settled => _jobs.Count > 0 ? DictationState.Processing : DictationState.Idle;

    /// <summary>Der letzte Druck hat eine Aufnahme während der Verarbeitung gestartet und ist keine 400 ms her.</summary>
    private bool InDoublePressWindow => _nextPressAt is { } at && _now() - at < DoublePressWindow;

    /// <summary>Zweiter Druck kurz nach dem Start der nächsten Aufnahme: Aufnahme verwerfen (nichts gesendet), Verbesserung überspringen.</summary>
    /// <returns>true, wenn es ein Doppeldruck war.</returns>
    public bool TryDoublePress()
    {
        if (!InDoublePressWindow)
        {
            return false;
        }
        _nextPressAt = null;
        if (IsRecording)
        {
            _recorder.Cancel();
            _recorded = TimeSpan.Zero;
            ActiveProfile = null;
            _log.Event("recording.cancel", ("reason", "double-press"));
        }
        foreach (var job in _jobs)
        {
            // Auch ein Diktat, das noch erkannt wird: seine Verbesserung endet sofort, wenn sie beginnt.
            job.Skip.Skip();
        }
        _log.Event("refine.skipped");
        State = Settled;
        return true;
    }

    /// <summary>Start während der Verarbeitung: Doppeldruck oder nächste Aufnahme.</summary>
    /// <returns>"skipped" (Doppeldruck), "busy" (kein Diktat in Verarbeitung) oder null = Aufnahme starten.</returns>
    private string? StartDuringProcessing()
    {
        if (!CanStartNext)
        {
            return "busy";
        }
        return TryDoublePress() ? "skipped" : null;
    }

    /// <summary>
    /// Stopp der nächsten Aufnahme innerhalb des Doppeldruck-Fensters (Halten: kurz antippen): ein Antippen, kein Diktat –
    /// verwerfen, ohne etwas zu senden. Ein weiterer Druck im Fenster überspringt dann die Verbesserung.
    /// </summary>
    private bool DiscardIfTap()
    {
        if (!InDoublePressWindow)
        {
            return false;
        }
        _recorder.Cancel();
        _recorded = TimeSpan.Zero;
        ActiveProfile = null;
        State = Settled;
        _log.Event("recording.cancel", ("reason", "tap"));
        return true;
    }

    /// <summary>Beginnt die Verarbeitung eines Diktats; die Ausgabe wartet auf die des vorigen.</summary>
    private Job StartJob(AudioBuffer audio)
    {
        // Zuerst der Zustand: endet die Aufnahme von außen, gibt der Tastenkürzel-Ablauf sein Ziel frei, bevor es gebunden wird.
        State = DictationState.Processing;
        var job = new Job(_output is IBindableOutput bindable ? bindable.Bind() : _output);
        _jobs.Add(job);
        var previous = _outputTurn;
        var turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outputTurn = turn.Task;
        job.Done = ProcessAsync(audio, job, previous, turn);
        return job;
    }

    /// <summary>Diktat fertig (Ausgabe, Fehler oder Abbruch): Reihenfolge freigeben, Zustand nachziehen.</summary>
    private void FinishJob(Job job)
    {
        _jobs.Remove(job);
        job.Cts.Dispose();
        if (IsRecording)
        {
            // Die laufende Aufnahme gehört schon zum nächsten Diktat: ihre Dauer und ihr Profil bleiben.
            return;
        }
        _recorded = TimeSpan.Zero;
        ActiveProfile = null;
        State = Settled;
    }

    /// <summary>Ein Diktat in Verarbeitung: eigener Abbruch, eigener Ausweg, eigene Ausgabe, eigenes Ergebnis.</summary>
    private sealed class Job(IOutputSink output)
    {
        public CancellationTokenSource Cts { get; } = new();

        public RefineSkip Skip { get; } = new();

        public IOutputSink Output { get; } = output;

        /// <summary>Die Verbesserung läuft (gesetzt aus dem Thread der Pipeline).</summary>
        public volatile bool Refining;

        public string? ErrorClass { get; set; }

        public Task Done { get; set; } = Task.CompletedTask;
    }
}
