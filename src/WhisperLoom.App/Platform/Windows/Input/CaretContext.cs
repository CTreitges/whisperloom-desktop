namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>Zeichen vor der Schreibmarke im fokussierten Feld (Leerzeichen davor, W2-OUT). Windows: UIA TextPattern; Tests: Fake.</summary>
public interface ICaretSource
{
    /// <returns>null = Feldanfang, keine Schreibmarke oder nicht lesbar.</returns>
    char? CharBeforeCaret();
}

/// <summary>
/// Abfrage mit Zeitgrenze auf einem Pool-Thread (wie <see cref="FocusProbe"/>): eine hängende App hält das Einfügen nicht auf,
/// jeder Fehler gilt als „nicht lesbar“ – dann kein Leerzeichen davor (lieber keins als ein falsches).
/// </summary>
public sealed class CaretContext(ICaretSource source, TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(300);

    public async Task<char?> CharBeforeAsync(CancellationToken ct)
    {
        try
        {
            return await Task.Run(source.CharBeforeCaret, ct).WaitAsync(timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
