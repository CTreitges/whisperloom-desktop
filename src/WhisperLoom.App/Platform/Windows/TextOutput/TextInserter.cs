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
}

/// <summary>
/// Wohin die letzte Eingabe ging (nur Metadaten, nie Text): gemerktes Ziel, tatsächliches Vordergrundfenster beim Senden,
/// dessen Prozess und die Fensterklasse des Steuerelements mit Tastaturfokus.
/// </summary>
public sealed record InsertReport(IntPtr Target, IntPtr Window, string Process, string FocusClass)
{
    public bool SameTarget => Window == Target;
}

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

    /// <summary>Landet der Text nur in der Zwischenablage, obwohl Einfügen gewollt war (Toast nötig)?</summary>
    public static bool IsFallback(this InsertOutcome outcome) =>
        outcome is InsertOutcome.TargetGone or InsertOutcome.TargetProtected or InsertOutcome.NotForeground or InsertOutcome.InputRejected;
}

/// <summary>
/// Fügt ein Diktat in das gemerkte Zielfenster ein: Zwischenablage sichern → Text privat setzen → Strg+V →
/// (Enter) → Zwischenablage wiederherstellen, sofern sie seitdem niemand geändert hat. Oder direkt tippen.
/// Ist das Ziel weg, geschützt (UIPI) oder nicht erreichbar, landet der Text in der Zwischenablage.
/// </summary>
// Doku: learn.microsoft.com/windows/win32/api/winuser/nf-winuser-sendinput (UIPI-Hinweis, abgerufen 2026-10-31).
public sealed class TextInserter(IClipboardAdapter clipboard, IKeySender keys, IWindowAdapter windows, TimeSpan? pasteDelay = null)
{
    /// <summary>Zeit, die das Ziel zum Lesen der Zwischenablage bekommt, bevor Enter bzw. die Wiederherstellung folgt.</summary>
    public static readonly TimeSpan DefaultPasteDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>Höchste Wartezeit auf losgelassene Modifier (sonst würde aus Strg+V z. B. Win+V).</summary>
    public static readonly TimeSpan ModifierWait = TimeSpan.FromSeconds(2);

    /// <summary>Ziel der letzten echten Eingabe (Strg+V bzw. Tippen); null, wenn keine gesendet wurde.</summary>
    public InsertReport? LastReport { get; private set; }

    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    public async Task<InsertOutcome> InsertAsync(IntPtr target, string text, InsertMode mode, bool pressEnter, CancellationToken ct)
    {
        LastReport = null;
        if (text.Length == 0)
        {
            return InsertOutcome.Empty;
        }
        if (mode == InsertMode.ClipboardOnly)
        {
            clipboard.SetText(text, @private: false);
            return InsertOutcome.ClipboardOnly;
        }
        if (target == IntPtr.Zero || !windows.Exists(target))
        {
            return Fallback(text, InsertOutcome.TargetGone);
        }
        if (windows.IsProtected(target))
        {
            return Fallback(text, InsertOutcome.TargetProtected);
        }
        await keys.WaitForModifiersReleasedAsync(ModifierWait, ct).ConfigureAwait(false);
        if (!windows.Activate(target))
        {
            return Fallback(text, InsertOutcome.NotForeground);
        }
        if (mode == InsertMode.Type)
        {
            LastReport = Describe(target);
            if (!keys.Type(text))
            {
                return Fallback(text, InsertOutcome.InputRejected);
            }
            if (pressEnter && windows.Foreground() == target)
            {
                keys.SendEnter();
            }
            return InsertOutcome.Typed;
        }

        var snapshot = clipboard.Save();
        // Wiederherstellen in jedem Fall (auch wenn Setzen scheitert oder abgebrochen wird) — außer der Text soll bewusst
        // in der Zwischenablage bleiben (abgewiesene Eingabe) oder jemand anderes hat inzwischen kopiert.
        var restore = true;
        try
        {
            clipboard.SetText(text, @private: true);
            var ours = clipboard.SequenceNumber;
            LastReport = Describe(target);
            if (!keys.SendCtrlV())
            {
                restore = false;
                return Fallback(text, InsertOutcome.InputRejected);
            }
            await Task.Delay(pasteDelay ?? DefaultPasteDelay, ct).ConfigureAwait(false);
            // Enter nur ins Ziel: hat der Nutzer inzwischen das Fenster gewechselt, träfe es (z. B. als Absenden) ein anderes.
            if (pressEnter && windows.Foreground() == target)
            {
                keys.SendEnter();
            }
            restore = clipboard.SequenceNumber == ours;
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

    private InsertOutcome Fallback(string text, InsertOutcome outcome)
    {
        clipboard.SetText(text, @private: false);
        return outcome;
    }
}
