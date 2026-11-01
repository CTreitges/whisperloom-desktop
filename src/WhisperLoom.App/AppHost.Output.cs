using WhisperLoom.App.Output;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App;

/// <summary>Ausgabe-Teil des Hosts (W2-OUT): Hinweise nach der Ausgabe und verlässliches Kopieren für Verlauf und Tray.</summary>
public sealed partial class AppHost
{
    /// <summary>Die Zwischenablage ohne Einfügen und ohne Hinweis (Verlauf „Kopieren“); null = <see cref="Output"/>.</summary>
    public IOutputSink? Clipboard { get; internal set; }

    /// <summary>Zeigt einen Hinweis (Windows: Toast über die Eingabe-Fenster, Linux: notify-send); null = nur merken.</summary>
    public Action<OutputHint>? HintPresenter { get; set; }

    /// <summary>Letzter Hinweis für <c>status --json</c>: "none", "copied", "password".</summary>
    public string LastHint { get; private set; } = OutputHints.Name(OutputHint.None);

    /// <summary>Wie oft „Kopieren“ aus dem Verlauf seit dem Start gelang bzw. scheiterte (Smoke, nie Text).</summary>
    public (int Ok, int Failed) CopyCount { get; private set; }

    /// <summary>Hinweis merken (Status) und anzeigen.</summary>
    internal void OnHint(OutputHint hint)
    {
        RecordHint(hint);
        if (hint != OutputHint.None)
        {
            HintPresenter?.Invoke(hint);
        }
    }

    /// <summary>Nur merken: den Toast zeigt schon die Windows-Eingabe (Einfügen per Tastenkürzel).</summary>
    internal void RecordHint(OutputHint hint) => LastHint = OutputHints.Name(hint);

    /// <summary>
    /// „Kopieren“ aus Verlauf bzw. Tray: Text verlässlich in die Zwischenablage (Wiederholungen und Gegenprobe stecken in der
    /// Zwischenablage-Senke), ohne Einfügen. Kein Text im Log.
    /// </summary>
    /// <returns>true = kopiert.</returns>
    public async Task<bool> CopyTextAsync(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }
        try
        {
            await (Clipboard ?? Output).WriteAsync(text, CancellationToken.None);
            CopyCount = (CopyCount.Ok + 1, CopyCount.Failed);
            Log.Event("copy", ("status", "ok"), ("chars", text.Length));
            return true;
        }
        catch (IOException e)
        {
            CopyCount = (CopyCount.Ok, CopyCount.Failed + 1);
            Log.Event("copy", ("status", "error"), ("errorClass", e.GetType().Name));
            return false;
        }
    }
}
