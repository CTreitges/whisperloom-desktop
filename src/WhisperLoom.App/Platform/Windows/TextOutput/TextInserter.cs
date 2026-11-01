using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>Ausgabemodus beim Diktat per Tastenkürzel (Einstellung <c>desktop.insertMode</c>).</summary>
public enum InsertMode
{
    /// <summary>Über die Zwischenablage einfügen (Strg+V), danach die alte Zwischenablage wiederherstellen.</summary>
    Paste,

    /// <summary>Direkt tippen (SendInput, Unicode).</summary>
    Type,

    /// <summary>Nur in die Zwischenablage legen.</summary>
    ClipboardOnly,
}

public enum InsertOutcome
{
    Empty,
    Pasted,
    Typed,
    ClipboardOnly,

    /// <summary>Zielfenster existiert nicht mehr: Text liegt in der Zwischenablage.</summary>
    TargetGone,

    /// <summary>Zielfenster läuft als Admin (UIPI blockt SendInput): Text liegt in der Zwischenablage.</summary>
    TargetProtected,

    /// <summary>Ziel ließ sich nicht nach vorn holen: Text liegt in der Zwischenablage.</summary>
    NotForeground,

    /// <summary>SendInput wurde abgewiesen: Text liegt in der Zwischenablage.</summary>
    InputRejected,

    /// <summary>Im Ziel hat kein Eingabefeld den Fokus (UI Automation): Text liegt in der Zwischenablage (W2-OUT).</summary>
    NoTextField,

    /// <summary>Fokus liegt in einem Passwortfeld: weder eingefügt noch kopiert, kein Verlaufseintrag (W2-OUT).</summary>
    PasswordField,
}

/// <summary>
/// Wohin die letzte Eingabe ging (nur Metadaten, nie Text): gemerktes Ziel, tatsächliches Vordergrundfenster beim Senden,
/// dessen Prozess und die Fensterklasse des Steuerelements mit Tastaturfokus.
/// </summary>
public sealed record InsertReport(IntPtr Target, IntPtr Window, string Process, string FocusClass)
{
    public bool SameTarget => Window == Target;
}

/// <summary>
/// „Markierung ersetzen“ (W2-FIX M-4): Das Ziel wird nicht nach vorn geholt. Ersetzt wird nur, wenn das Zielfenster noch vorn
/// ist und das fokussierte Element das beim Erfassen ist; sonst Zwischenablage + Hinweis (<see cref="InsertOutcome.NotForeground"/>).
/// </summary>
/// <param name="ElementId">UIA-RuntimeId beim Erfassen; null = unbekannt (dann zählt nur das Fenster).</param>
public sealed record InsertGuard(string? ElementId);

public static class InsertModes
{
    public static InsertMode FromKey(string? key) => key switch
    {
        "type" => InsertMode.Type,
        "clipboard" => InsertMode.ClipboardOnly,
        _ => InsertMode.Paste,
    };

    public static string Key(this InsertMode mode) => mode switch
    {
        InsertMode.Type => "type",
        InsertMode.ClipboardOnly => "clipboard",
        _ => "paste",
    };

    /// <summary>Landet der Text nur in der Zwischenablage, obwohl Einfügen gewollt war (Hinweis „Kopiert – Strg+V“)?</summary>
    public static bool IsFallback(this InsertOutcome outcome) =>
        outcome is InsertOutcome.TargetGone or InsertOutcome.TargetProtected or InsertOutcome.NotForeground or InsertOutcome.InputRejected
            or InsertOutcome.NoTextField;

    /// <summary>Hinweis nach dem Einfügen (<see cref="OutputDecision"/>): Kopiert, Passwortfeld oder keiner.</summary>
    public static OutputHint Hint(this InsertOutcome outcome) => outcome switch
    {
        InsertOutcome.PasswordField => OutputHint.PasswordField,
        InsertOutcome.ClipboardOnly => OutputHint.Copied,
        _ when outcome.IsFallback() => OutputHint.Copied,
        _ => OutputHint.None,
    };
}

/// <summary>
/// Fügt ein Diktat in das gemerkte Zielfenster ein: Zwischenablage sichern → Text privat setzen → Strg+V →
/// (Enter) → Zwischenablage wiederherstellen, sofern sie seitdem niemand geändert hat. Oder direkt tippen.
/// Kein Diktat geht verloren (W2-OUT, <see cref="OutputDecision"/>): nach dem Aktivieren prüft UI Automation den Fokus.
/// Passwortfeld → nichts; kein Eingabefeld, Ziel weg, geschützt (UIPI) oder nicht erreichbar → Zwischenablage;
/// unbekannter Fokus oder <c>output.alwaysCopy</c> → einfügen und den Text danach in der Zwischenablage lassen.
/// </summary>
/// <param name="focus">Fokus-Klasse nach dem Aktivieren; null = unbekannt.</param>
// Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput (UIPI-Hinweis, abgerufen 2026-10-31).
public sealed class TextInserter(
    IClipboardAdapter clipboard,
    IKeySender keys,
    IWindowAdapter windows,
    TimeSpan? pasteDelay = null,
    IFocusProbe? focus = null,
    Func<TimeSpan, Task>? copyDelay = null,
    CaretContext? caret = null)
{
    /// <summary>Zeit, die das Ziel zum Lesen der Zwischenablage bekommt, bevor Enter bzw. die Wiederherstellung folgt.</summary>
    public static readonly TimeSpan DefaultPasteDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Höchste Wartezeit auf losgelassene Modifier (sonst würde aus Strg+V z. B. Win+V).</summary>
    public static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(2);

    /// <summary>Ziel der letzten echten Eingabe (Strg+V bzw. Tippen); null, wenn keine gesendet wurde.</summary>
    public InsertReport? LastReport { get; private set; }

    /// <summary>Fokus-Klasse der letzten Einfügung (Status/Smoke); null = nicht geprüft.</summary>
    public FocusKind? LastFocus { get; private set; }

    /// <param name="alwaysCopy"><c>output.alwaysCopy</c>: nach erfolgreichem Einfügen auch in der Zwischenablage lassen.</param>
    /// <param name="leadingSpace">Diktat: Leerzeichen davor, wenn direkt an ein Wort angefügt wird (Zeichen vor der Schreibmarke
    /// per UIA TextPattern; ohne Auskunft keins).</param>
    /// <param name="guard">Markierung ersetzen: nicht aktivieren, Fenster und Element müssen stimmen (W2-FIX M-4).</param>
    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    public async Task<InsertOutcome> InsertAsync(
        IntPtr target, string text, InsertMode mode, bool pressEnter, CancellationToken ct, bool alwaysCopy = false, bool leadingSpace = false,
        InsertGuard? guard = null)
    {
        LastReport = null;
        LastFocus = null;
        if (text.Length == 0)
        {
            return InsertOutcome.Empty;
        }
        if (mode == InsertMode.ClipboardOnly)
        {
            // „Nie Passwort“ gilt auch hier (Prüfung W2-OUT B2): ist das Ziel noch vorn, Fokus prüfen – ohne es zu aktivieren.
            if (target != IntPtr.Zero && windows.Foreground() == target)
            {
                var focused = await (focus ?? UnknownFocusProbe.Instance).ProbeAsync(ct).ConfigureAwait(false);
                LastFocus = focused;
                if (focused == FocusKind.Password)
                {
                    return InsertOutcome.PasswordField;
                }
            }
            await CopyAsync(text);
            return InsertOutcome.ClipboardOnly;
        }
        if (target == IntPtr.Zero || !windows.Exists(target))
        {
            return await FallbackAsync(text, InsertOutcome.TargetGone);
        }
        if (windows.IsProtected(target))
        {
            return await FallbackAsync(text, InsertOutcome.TargetProtected);
        }
        await keys.WaitForModifiersReleasedAsync(ModifierWait, ct).ConfigureAwait(false);
        if (guard is not null ? !await OnTargetAsync(target, guard, ct).ConfigureAwait(false) : !windows.Activate(target))
        {
            return await FallbackAsync(text, InsertOutcome.NotForeground);
        }
        var kind = await (focus ?? UnknownFocusProbe.Instance).ProbeAsync(ct).ConfigureAwait(false);
        LastFocus = kind;
        if (kind == FocusKind.Password)
        {
            return InsertOutcome.PasswordField;
        }
        if (!OutputDecision.ShouldInsert(kind, canInsert: true))
        {
            return await FallbackAsync(text, InsertOutcome.NoTextField);
        }
        if (leadingSpace && caret is not null)
        {
            text = OutputDecision.WithLeadingSpace(text, await caret.CharBeforeAsync(ct).ConfigureAwait(false));
        }
        // Unbekannter Fokus (UIA meldet nichts Brauchbares) oder „immer kopieren“: der Text bleibt danach in der Zwischenablage.
        // Im Browser und in Electron-Apps („Vorsichtig im Browser“, Entscheidung Christof): unbekannter Fokus → alte Zwischenablage zurück.
        var cautious = OutputDecision.IsCautious(windows.ProcessName(target), windows.WindowClass(target));
        var keep = OutputDecision.Decide(kind, InsertAttempt.Inserted, alwaysCopy, cautious).Copy;
        // W2-FIX N-5: Fokus-Prüfung und Leerzeichen-Abfrage dauern bis 600 ms; direkt vor dem Senden noch einmal prüfen.
        if (!await StillOnTargetAsync(target, guard, ct).ConfigureAwait(false))
        {
            return await FallbackAsync(text, InsertOutcome.NotForeground);
        }
        if (mode == InsertMode.Type)
        {
            LastReport = Describe(target);
            // Ein getippter Zeilenumbruch ist Umschalt+Enter — in einer Konsole führt das die Zeile aus (Review WLD-FIX R-1).
            if (Features.Rewrite.SelectionCapture.IsConsole(windows.ProcessName(target)))
            {
                text = OneLine(text);
            }
            if (!keys.Type(text))
            {
                return await FallbackAsync(text, InsertOutcome.InputRejected);
            }
            if (pressEnter && windows.Foreground() == target)
            {
                keys.SendEnter();
            }
            if (keep)
            {
                await TryCopyAsync(text);
            }
            return InsertOutcome.Typed;
        }

        var snapshot = clipboard.Save();
        // Wiederherstellen in jedem Fall (auch wenn Setzen scheitert oder abgebrochen wird) — außer der Text soll bewusst
        // in der Zwischenablage bleiben (abgewiesene Eingabe, unbekannter Fokus, „immer kopieren“) oder jemand anderes hat
        // inzwischen kopiert.
        var restore = true;
        try
        {
            clipboard.SetText(text, @private: true);
            var ours = clipboard.SequenceNumber;
            if (windows.Foreground() != target)
            {
                // N-5: zwischen Prüfung und Strg+V gewechselt. Text öffentlich in die Zwischenablage statt ins falsche Fenster.
                restore = false;
                return await FallbackAsync(text, InsertOutcome.NotForeground);
            }
            LastReport = Describe(target);
            if (!keys.SendCtrlV())
            {
                restore = false;
                return await FallbackAsync(text, InsertOutcome.InputRejected);
            }
            await Task.Delay(pasteDelay ?? DefaultPasteDelay, ct).ConfigureAwait(false);
            // Enter nur ins Ziel: hat der Nutzer inzwischen das Fenster gewechselt, träfe es (z. B. als Absenden) ein anderes.
            if (pressEnter && windows.Foreground() == target)
            {
                keys.SendEnter();
            }
            restore = clipboard.SequenceNumber == ours;
            if (restore && keep)
            {
                // Statt der alten Zwischenablage bleibt der Text – öffentlich, damit Win+V ihn zeigt.
                restore = false;
                // Scheitert das, liegt der Text noch privat in der Zwischenablage (eben eingefügt) – nichts geht verloren.
                await TryCopyAsync(text);
            }
            return InsertOutcome.Pasted;
        }
        finally
        {
            if (restore)
            {
                Restore(snapshot);
            }
        }
    }

    /// <summary>Ziel vorn (ohne es zu holen) und, wenn bekannt, dasselbe fokussierte Element wie beim Erfassen?</summary>
    private async Task<bool> OnTargetAsync(IntPtr target, InsertGuard guard, CancellationToken ct)
    {
        if (windows.Foreground() != target)
        {
            return false;
        }
        if (guard.ElementId is null)
        {
            return true;
        }
        var now = await (focus ?? UnknownFocusProbe.Instance).FocusedElementAsync(ct).ConfigureAwait(false);
        return now == guard.ElementId && windows.Foreground() == target;
    }

    private async Task<bool> StillOnTargetAsync(IntPtr target, InsertGuard? guard, CancellationToken ct) =>
        guard is null ? windows.Foreground() == target : await OnTargetAsync(target, guard, ct).ConfigureAwait(false);

    /// <summary>Zeilenumbrüche (\r\n, \n, \r) als je ein Leerzeichen.</summary>
    internal static string OneLine(string text) => text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Replace('\r', ' ');

    private InsertReport Describe(IntPtr target)
    {
        var window = windows.Foreground();
        return new InsertReport(target, window, windows.ProcessName(window), windows.FocusClass(window));
    }

    private void Restore(ClipboardSnapshot snapshot)
    {
        try
        {
            clipboard.Restore(snapshot);
        }
        catch (IOException)
        {
            // Belegt: dann bleibt das Diktat in der Zwischenablage.
        }
    }

    /// <summary>Text in die Zwischenablage, mit Wiederholungen solange sie belegt ist (<see cref="Output.ClipboardCopier"/>).</summary>
    /// <exception cref="IOException">Auch nach allen Versuchen belegt.</exception>
    private async Task CopyAsync(string text)
    {
        var copier = new Output.ClipboardCopier(t =>
        {
            clipboard.SetText(t, @private: false);
            return Task.CompletedTask;
        }, delay: copyDelay);
        if (!await copier.CopyAsync(text).ConfigureAwait(false))
        {
            throw new IOException("Zwischenablage ist belegt.");
        }
    }

    /// <summary>Zusätzliche Kopie nach erfolgreichem Einfügen; belegt = ohne Kopie weiter (eingefügt ist eingefügt).</summary>
    private async Task TryCopyAsync(string text)
    {
        try
        {
            await CopyAsync(text).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Zwischenablage auch nach allen Versuchen belegt.
        }
    }

    private async Task<InsertOutcome> FallbackAsync(string text, InsertOutcome outcome)
    {
        await CopyAsync(text).ConfigureAwait(false);
        return outcome;
    }
}
