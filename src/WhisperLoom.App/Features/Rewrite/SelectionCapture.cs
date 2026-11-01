using System.Diagnostics;
using WhisperLoom.App.Platform.Windows.TextOutput;

namespace WhisperLoom.App.Features.Rewrite;

/// <summary>Ergebnis der Erfassung: die Markierung oder der Lokalisierungs-Schlüssel des Toasts, warum es keine gibt.</summary>
public sealed record Capture(string? Text, string ToastKey = "Rewrite_NothingSelected")
{
    public static Capture Nothing { get; } = new(Text: null);
}

/// <summary>
/// Holt die Markierung des Zielfensters per simuliertem Strg+C bzw. Strg+Einfg: Zwischenablage sichern → Kopier-Kombi →
/// auf eine Änderung warten → Text lesen → Zwischenablage wiederherstellen. Ändert sich die Zwischenablage nicht (nichts
/// markiert) oder kommt kein Text, gibt es keine Markierung. Die Kopier-Kombi geht nur ans gemerkte Ziel: ist nach dem
/// Warten auf die Modifier ein anderes Fenster vorn oder das Ziel ein Admin-Fenster, unterbleibt sie. Das Kopieren landet wie
/// jedes Kopieren des Nutzers im Zwischenablage-Verlauf des Ziels.
/// </summary>
public sealed class SelectionCapture(IClipboardAdapter clipboard, IKeySender keys, IWindowAdapter windows, TimeSpan? copyWait = null)
{
    /// <summary>So lange bekommt das Ziel, um auf die Kopier-Kombi die Zwischenablage zu setzen.</summary>
    public static readonly TimeSpan DefaultCopyWait = TimeSpan.FromMilliseconds(600);

    /// <summary>Pause vor dem zweiten Versuch, die Zwischenablage wiederherzustellen (ein anderes Programm hält sie kurz).</summary>
    public static readonly TimeSpan RestoreRetryDelay = TimeSpan.FromMilliseconds(150);

    // Konsolen (Strg+C = Abbruch) und Editoren mit eingebettetem Terminal; alle kopieren mit Strg+Einfg.
    private static readonly HashSet<string> CtrlInsertPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh", "wsl", "wslhost",
        "Code", "idea64", "pycharm64", "rider64", "webstorm64", "clion64", "goland64", "phpstorm64", "datagrip64",
    };

    /// <summary>
    /// Programm, in dem Strg+C ohne Markierung den laufenden Befehl abbricht (Konsole, Terminal im Editor), Prozessname ohne
    /// .exe: dort kopiert die Erfassung mit Strg+Einfg.
    /// </summary>
    public static bool IsConsole(string process) => CtrlInsertPrograms.Contains(Profiles.AppProfiles.NormalizeProcess(process));

    /// <param name="target">Das Fenster, in dem der Nutzer das Kürzel gedrückt hat.</param>
    /// <param name="console">Strg+Einfg statt Strg+C (<see cref="IsConsole"/>).</param>
    /// <exception cref="IOException">Zwischenablage belegt (beim Sichern).</exception>
    public async Task<Capture> CaptureAsync(IntPtr target, bool console, CancellationToken ct)
    {
        // Noch gehaltenes Strg+Alt+Umschalt des Kürzels machte aus Strg+C eine andere Kombi.
        await keys.WaitForModifiersReleasedAsync(TextInserter.ModifierWait, ct).ConfigureAwait(false);
        if (windows.Foreground() != target)
        {
            return new Capture(null, "Rewrite_TargetChanged");
        }
        if (windows.IsProtected(target))
        {
            return new Capture(null, "Rewrite_TargetProtected");
        }
        var snapshot = clipboard.Save();
        var before = clipboard.SequenceNumber;
        if (!keys.SendCopy(ctrlInsert: console))
        {
            return Capture.Nothing;
        }
        var wait = copyWait ?? DefaultCopyWait;
        var watch = Stopwatch.StartNew();
        while (clipboard.SequenceNumber == before)
        {
            if (watch.Elapsed >= wait)
            {
                return Capture.Nothing;
            }
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        string? text;
        try
        {
            text = clipboard.GetText();
        }
        catch (IOException)
        {
            text = null;
        }
        if (!await RestoreAsync(snapshot, ct).ConfigureAwait(false))
        {
            return new Capture(null, "Rewrite_RestoreFailed");
        }
        return string.IsNullOrWhiteSpace(text) ? Capture.Nothing : new Capture(text);
    }

    /// <summary>Zwei Versuche; scheitern beide, liegt die Markierung in der Zwischenablage (Toast statt Umschreiben).</summary>
    private async Task<bool> RestoreAsync(ClipboardSnapshot snapshot, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                clipboard.Restore(snapshot);
                return true;
            }
            catch (IOException) when (attempt == 0)
            {
                await Task.Delay(RestoreRetryDelay, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return false;
            }
        }
    }
}
