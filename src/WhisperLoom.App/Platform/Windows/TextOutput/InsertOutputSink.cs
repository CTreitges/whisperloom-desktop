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
    : IOutputSink, IPrivateAwareOutput, Services.IBindableOutput
{
    private IntPtr _target;
    private bool? _pressEnter;
    private bool _armed;
    private InsertMode? _mode;
    private bool _keepCopy;
    private bool _trailingSpace;
    private bool _leadingSpace;
    private InsertGuard? _guard;

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
    /// <param name="leadingSpace">Leerzeichen davor, wenn direkt an ein Wort angefügt wird (nur Diktate, wie Android).</param>
    /// <param name="guard">Markierung ersetzen: nur ins unveränderte Ziel, ohne es zu holen (W2-FIX M-4).</param>
    public void Arm(IntPtr target, bool? pressEnter = null, InsertMode? mode = null, bool keepCopy = false, bool trailingSpace = false,
        bool leadingSpace = false, InsertGuard? guard = null)
    {
        _guard = guard;
        _leadingSpace = leadingSpace;
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

    /// <summary>
    /// Bindet das gemerkte Ziel an ein Diktat (W3-NEXT): dessen Ausgabe geht später dorthin, auch wenn inzwischen das nächste
    /// Diktat ein neues Ziel gemerkt hat. Danach ist diese Senke wieder frei.
    /// </summary>
    public IOutputSink Bind()
    {
        var arming = _armed ? new Arming(_target, _pressEnter, _mode, _keepCopy, _trailingSpace, _leadingSpace, _guard) : null;
        _armed = false;
        return new BoundSink(this, arming);
    }

    public async Task WriteAsync(string text, CancellationToken ct)
    {
        var arming = _armed ? new Arming(_target, _pressEnter, _mode, _keepCopy, _trailingSpace, _leadingSpace, _guard) : null;
        _armed = false;
        await WriteCoreAsync(arming, text, ct).ConfigureAwait(false);
    }

    /// <returns>Die Ausgabe ging in ein Passwortfeld.</returns>
    private async Task<bool> WriteCoreAsync(Arming? a, string text, CancellationToken ct)
    {
        LastWasPrivate = false;
        if (a is null)
        {
            await inner.WriteAsync(text, ct).ConfigureAwait(false);
            return false;
        }
        var settings = output?.Invoke();
        var outcome = await inserter.InsertAsync(
            a.Target, Core.Output.OutputDecision.WithTrailingSpace(text, a.TrailingSpace), a.Mode ?? mode(), EffectivePressEnter(a.PressEnter, settings), ct,
            a.KeepCopy || (settings?.AlwaysCopy ?? false), a.LeadingSpace, a.Guard).ConfigureAwait(false);
        LastOutcome = outcome;
        LastWasPrivate = outcome == InsertOutcome.PasswordField;
        Inserted?.Invoke(outcome);
        return LastWasPrivate;
    }

    private sealed record Arming(IntPtr Target, bool? PressEnter, InsertMode? Mode, bool KeepCopy, bool TrailingSpace, bool LeadingSpace, InsertGuard? Guard);

    /// <summary>Ausgabe eines Diktats mit dem beim Stopp gebundenen Ziel (<see cref="Bind"/>); einmalig wie <see cref="Arm"/>.</summary>
    private sealed class BoundSink(InsertOutputSink owner, Arming? arming) : IOutputSink, IPrivateAwareOutput
    {
        private Arming? _arming = arming;

        public string Kind => owner.Kind;

        public bool LastWasPrivate { get; private set; }

        public async Task WriteAsync(string text, CancellationToken ct)
        {
            var a = _arming;
            _arming = null;
            LastWasPrivate = await owner.WriteCoreAsync(a, text, ct).ConfigureAwait(false);
        }
    }
}
