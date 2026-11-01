using System.Diagnostics;
using WhisperLoom.App.Platform.Windows.TextOutput;

namespace WhisperLoom.App.Features.Rewrite;

/// <summary>
/// Holt die Markierung des Vordergrundfensters per simuliertem Strg+C: Zwischenablage sichern → Strg+C → auf eine Änderung
/// warten → Text lesen → Zwischenablage wiederherstellen. Ändert sich die Zwischenablage nicht (nichts markiert) oder kommt
/// kein Text, ist das Ergebnis null. Strg+C landet wie jedes Kopieren des Nutzers im Zwischenablage-Verlauf des Ziels.
/// </summary>
public sealed class SelectionCapture(IClipboardAdapter clipboard, IKeySender keys, TimeSpan? copyWait = null)
{
    /// <summary>So lange bekommt das Ziel, um auf Strg+C die Zwischenablage zu setzen.</summary>
    public static readonly TimeSpan DefaultCopyWait = TimeSpan.FromMilliseconds(600);

    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    public async Task<string?> CaptureAsync(CancellationToken ct)
    {
        // Noch gehaltenes Strg+Alt+Umschalt des Kürzels machte aus Strg+C eine andere Kombi.
        await keys.WaitForModifiersReleasedAsync(TextInserter.ModifierWait, ct).ConfigureAwait(false);
        var snapshot = clipboard.Save();
        var before = clipboard.SequenceNumber;
        if (!keys.SendCtrlC())
        {
            return null;
        }
        var wait = copyWait ?? DefaultCopyWait;
        var watch = Stopwatch.StartNew();
        while (clipboard.SequenceNumber == before)
        {
            if (watch.Elapsed >= wait)
            {
                return null;
            }
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        try
        {
            var text = clipboard.GetText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            clipboard.Restore(snapshot);
        }
    }
}
