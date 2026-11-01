using WhisperLoom.App.Output;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>
/// Ausgabe für Windows: Diktate per Tastenkürzel gehen über den <see cref="TextInserter"/> ins gemerkte Zielfenster
/// (<see cref="Arm"/>), alle anderen (Tray, CLI) wie bisher an die innere Senke (Zwischenablage).
/// Enter danach und „immer kopieren“ kommen aus <c>output.*</c>; ein App-Profil kann Enter überschreiben (W2-OUT).
/// </summary>
/// <param name="output">Aktuelle Ausgabe-Einstellungen; null = beides aus.</param>
public sealed class InsertOutputSink(IOutputSink inner, TextInserter inserter, Func<InsertMode> mode, Func<OutputSettings>? output = null)
    : IOutputSink, IPrivateAwareOutput
{
    private IntPtr _target;
    private bool? _pressEnter;
    private bool _armed;
    private InsertMode? _mode;
    private bool _keepCopy;
    private bool _trailingSpace;

    public string Kind => inner.Kind;

    /// <summary>Ergebnis des letzten Einfügens (für Hinweis und Status).</summary>
    public event Action<InsertOutcome>? Inserted;

    public InsertOutcome? LastOutcome { get; private set; }

    /// <summary>Wohin die letzte Eingabe ging (Fenster, Prozess, Fokus-Klasse); null ohne Eingabe.</summary>
    public InsertReport? LastReport => inserter.LastReport;

    /// <summary>Fokus-Klasse der letzten Einfügung (UI Automation); null = nicht geprüft.</summary>
    public Core.Output.FocusKind? LastFocus => inserter.LastFocus;

    /// <summary>Ziel des letzten <see cref="Arm"/> (Tray-Verlauf fügt dorthin ein); 0 = noch keins.</summary>
    public IntPtr LastTarget { get; private set; }

    /// <summary>Die letzte Ausgabe ging in ein Passwortfeld: nicht in den Verlauf (wie Android <c>HistoryPolicy</c>).</summary>
    public bool LastWasPrivate { get; private set; }

    /// <summary>Das nächste Diktat geht in <paramref name="target"/>; einmalig.</summary>
    /// <param name="pressEnter">Enter danach: null = Erben (<c>output.pressEnter</c>), sonst Wert des App-Profils.</param>
    /// <param name="mode">Ausgabemodus des App-Profils; null = Einstellung.</param>
    /// <param name="keepCopy">Text auf jeden Fall auch in der Zwischenablage lassen (Tray-Verlauf); sonst <c>output.alwaysCopy</c>.</param>
    /// <param name="trailingSpace">Leerzeichen anhängen (<c>polish.trailingSpace</c>, nur Diktate, wie Android).</param>
    public void Arm(IntPtr target, bool? pressEnter = null, InsertMode? mode = null, bool keepCopy = false, bool trailingSpace = false)
    {
        _keepCopy = keepCopy;
        _trailingSpace = trailingSpace;
        _target = target;
        LastTarget = target;
        _pressEnter = pressEnter;
        _mode = mode;
        _armed = true;
    }

    public void Disarm() => _armed = false;

    /// <summary>Profil-Wert oder, bei Erben, die globale Einstellung.</summary>
    public static bool EffectivePressEnter(bool? profile, OutputSettings? settings) => profile ?? settings?.PressEnter ?? false;

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        LastWasPrivate = false;
        if (!_armed)
        {
            await inner.WriteAsync(text, ct).ConfigureAwait(false);
            return;
        }
        _armed = false;
        var settings = output?.Invoke();
        var outcome = await inserter.InsertAsync(
            _target, Core.Output.OutputDecision.WithTrailingSpace(text, _trailingSpace), _mode ?? mode(), EffectivePressEnter(_pressEnter, settings), ct, _keepCopy || (settings?.AlwaysCopy ?? false)).ConfigureAwait(false);
        LastOutcome = outcome;
        LastWasPrivate = outcome == InsertOutcome.PasswordField;
        Inserted?.Invoke(outcome);
    }
}
