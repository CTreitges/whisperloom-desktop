namespace WhisperLoom.Core.Pipeline;

/// <summary>
/// Ausweg während der Textverbesserung (Port von <c>RefineSkip.kt</c>): <see cref="Skip"/> lässt das Diktat sofort mit dem
/// Text ohne KI zurückkehren, statt auf eine langsame Rechnung oder ein Zeitlimit zu warten. Die laufende Arbeit wird über
/// ihren Token abgebrochen (Online-Anfrage, lokales Modell).
/// </summary>
public sealed class RefineSkip
{
    private readonly CancellationTokenSource _skip = new();

    public bool IsSkipped => _skip.IsCancellationRequested;

    /// <summary>Aus jedem Thread; mehrfach harmlos.</summary>
    public void Skip() => _skip.Cancel();

    /// <summary>
    /// Führt <paramref name="work"/> aus und wartet auf sein Ergebnis oder <see cref="Skip"/>, was zuerst kommt.
    /// null = übersprungen. Fehler kommen durch, solange nicht übersprungen wurde; Abbruch über <paramref name="ct"/> bleibt Abbruch.
    /// </summary>
    internal async Task<T?> RaceAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        where T : class
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _skip.Token);
        var skipped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = _skip.Token.Register(() => skipped.TrySetResult());
        var task = work(linked.Token);
        if (await Task.WhenAny(task, skipped.Task).ConfigureAwait(false) == task)
        {
            try
            {
                return await task.ConfigureAwait(false);
            }
            catch (Exception) when (IsSkipped && !ct.IsCancellationRequested)
            {
                return null;
            }
        }
        // Die verworfene Arbeit läuft über ihren Token aus; ihr Fehler interessiert niemanden mehr.
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        ct.ThrowIfCancellationRequested();
        return null;
    }
}
