using System.Collections.Concurrent;
using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Pipeline für W3-NEXT: Erkennung sofort („Diktat n“ in Aufruf-Reihenfolge), die Verbesserung wartet, bis der Test sie mit
/// <see cref="Release"/> freigibt („[KI] Diktat n“) oder bis übersprungen wird (Rohtext, wie die echte Pipeline).
/// </summary>
internal sealed class GatedRefinePipeline : IDictationPipeline, ISkippablePipeline
{
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _gates = new();
    private readonly ConcurrentDictionary<int, bool> _refining = new();
    private readonly ConcurrentDictionary<int, bool> _finished = new();
    private int _runs;

    /// <summary>Gestartete Läufe (= gesendetes Audio).</summary>
    public int Runs => Volatile.Read(ref _runs);

    /// <summary>Die Verbesserung von Diktat <paramref name="n"/> läuft.</summary>
    public bool Refining(int n) => _refining.ContainsKey(n);

    /// <summary>Diktat <paramref name="n"/> ist durch die Pipeline (Ergebnis da, Ausgabe kann noch warten).</summary>
    public bool Finished(int n) => _finished.ContainsKey(n);

    public void Release(int n) => Gate(n).TrySetResult();

    /// <summary>Diktat <paramref name="n"/> scheitert wiederholbar (Netz), sobald es freigegeben ist.</summary>
    public void FailWithNetwork(int n)
    {
        _fail[n] = true;
        Release(n);
    }

    private readonly ConcurrentDictionary<int, bool> _fail = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _recognitionGates = new();
    private readonly ConcurrentDictionary<int, bool> _recognizing = new();

    /// <summary>Die Erkennung wartet, bis der Test sie mit <see cref="ReleaseRecognition"/> freigibt (TS-WTRAY).</summary>
    public bool HoldRecognition { get; init; }

    /// <summary>Keine Verbesserung geplant (Stufe aus): nach der Erkennung sofort der Rohtext, ohne Beginn der Verbesserung.</summary>
    public bool NoRefine { get; init; }

    /// <summary>Diktat <paramref name="n"/> wird gerade erkannt (nur mit <see cref="HoldRecognition"/>).</summary>
    public bool Recognizing(int n) => _recognizing.ContainsKey(n);

    public void ReleaseRecognition(int n) =>
        _recognitionGates.GetOrAdd(n, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public Task<DictationResult> RunAsync(DictationRequest request, CancellationToken ct) =>
        throw new InvalidOperationException("ohne Ausweg aufgerufen");

    public async Task<DictationResult> RunAsync(DictationRequest request, RefineSkip skip, Action? onRefineStart, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref _runs);
        var raw = $"Diktat {n}";
        await Task.Yield();
        if (HoldRecognition)
        {
            _recognizing[n] = true;
            await _recognitionGates.GetOrAdd(n, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(ct);
            _recognizing.TryRemove(n, out _);
        }
        if (NoRefine)
        {
            _finished[n] = true;
            return new DictationResult(raw, "de", request.Audio.DurationMs, new RefinedText(request.Refinement, raw), "Fake-STT");
        }
        onRefineStart?.Invoke();
        _refining[n] = true;
        var gate = Gate(n).Task;
        while (!skip.IsSkipped && !gate.IsCompleted)
        {
            await Task.Delay(5, ct);
        }
        _finished[n] = true;
        if (_fail.ContainsKey(n))
        {
            throw new ApiNetworkException("Fake-Netzfehler");
        }
        var text = skip.IsSkipped ? raw : "[KI] " + raw;
        return new DictationResult(raw, "de", request.Audio.DurationMs,
            new RefinedText(request.Refinement, text, skip.IsSkipped ? null : "Fake-LLM"), "Fake-STT");
    }

    public Task<RefinedText> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
        Task.FromResult(new RefinedText(refinement, raw));

    private TaskCompletionSource Gate(int n) => _gates.GetOrAdd(n, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
}

internal static class Waits
{
    /// <summary>Bis zu 10 s (im Gesamtlauf unter Last, wie ComfortTest).</summary>
    public static async Task For(Func<bool> done)
    {
        for (var i = 0; i < 1000 && !done(); i++)
        {
            await Task.Delay(10);
        }
        Assert.True(done());
    }

    public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    public static Task Within(this Task task) => task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}
