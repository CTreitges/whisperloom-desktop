using WhisperLoom.App.Output;

namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>
/// Ausgabe für Windows: Diktate per Tastenkürzel gehen über den <see cref="TextInserter"/> ins gemerkte Zielfenster
/// (<see cref="Arm"/>), alle anderen (Tray, CLI) wie bisher an die innere Senke (Zwischenablage).
/// </summary>
public sealed class InsertOutputSink(IOutputSink inner, TextInserter inserter, Func<InsertMode> mode) : IOutputSink
{
    private IntPtr _target;
    private bool _pressEnter;
    private bool _armed;

    public string Kind => inner.Kind;

    /// <summary>Ergebnis des letzten Einfügens (für den Toast und den Status).</summary>
    public event Action<InsertOutcome>? Inserted;

    public InsertOutcome? LastOutcome { get; private set; }

    /// <summary>Wohin die letzte Eingabe ging (Fenster, Prozess, Fokus-Klasse); null ohne Eingabe.</summary>
    public InsertReport? LastReport => inserter.LastReport;

    /// <summary>Das nächste Diktat geht in <paramref name="target"/>; einmalig.</summary>
    public void Arm(IntPtr target, bool pressEnter = false)
    {
        _target = target;
        _pressEnter = pressEnter;
        _armed = true;
    }

    /// <summary>Enter nach dem Einfügen nachträglich an- oder abschalten (Strg+Enter im Overlay).</summary>
    public void SetPressEnter(bool pressEnter) => _pressEnter = pressEnter;

    public void Disarm() => _armed = false;

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        if (!_armed)
        {
            await inner.WriteAsync(text, ct).ConfigureAwait(false);
            return;
        }
        _armed = false;
        var outcome = await inserter.InsertAsync(_target, text, mode(), _pressEnter, ct).ConfigureAwait(false);
        LastOutcome = outcome;
        Inserted?.Invoke(outcome);
    }
}
