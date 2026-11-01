namespace WhisperLoom.App.Output;

/// <summary>
/// Text verlässlich in die Zwischenablage (W2-OUT, „Kopieren funktioniert“): mehrere Versuche mit wachsender Pause, solange
/// ein anderes Programm die Zwischenablage hält (Zwischenablage-Manager, Remote-Desktop, Office), und — wo lesbar — eine
/// Gegenprobe, dass wirklich unser Text drinsteht. Erst dann gilt „Kopiert“.
/// </summary>
/// <param name="set">Schreibt den Text; <see cref="IOException"/> = belegt.</param>
/// <param name="read">Liest zur Gegenprobe; null = keine Gegenprobe (Linux-Werkzeuge).</param>
/// <param name="delay">Pause zwischen Versuchen (Tests: sofort).</param>
public sealed class ClipboardCopier(Func<string, Task> set, Func<Task<string?>>? read = null, Func<TimeSpan, Task>? delay = null)
{
    /// <summary>Pausen zwischen den Versuchen: zusammen ≈ 1,5 s (dazu die kurzen Öffnen-Versuche je Schreiben).</summary>
    public static readonly IReadOnlyList<TimeSpan> Backoff =
        [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(400),
         TimeSpan.FromMilliseconds(800)];

    /// <summary>Versuche des letzten Aufrufs (Log/Status, nie Text).</summary>
    public int LastAttempts { get; private set; }

    /// <returns>true = Text liegt nachweislich (bzw. ohne Gegenprobe: geschrieben) in der Zwischenablage.</returns>
    public async Task<bool> CopyAsync(string text)
    {
        for (var attempt = 0; ; attempt++)
        {
            LastAttempts = attempt + 1;
            try
            {
                await set(text).ConfigureAwait(false);
                if (read is null || await read().ConfigureAwait(false) == text)
                {
                    return true;
                }
            }
            catch (IOException)
            {
                // Belegt: nächster Versuch.
            }
            if (attempt >= Backoff.Count)
            {
                return false;
            }
            await (delay ?? Task.Delay)(Backoff[attempt]).ConfigureAwait(false);
        }
    }
}
