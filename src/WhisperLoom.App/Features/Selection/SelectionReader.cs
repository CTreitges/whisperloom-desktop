using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Features.Selection;

/// <summary>Auskunft von UI Automation über die Markierung: ohne TextPattern keine Aussage (<see cref="HasTextPattern"/> false).</summary>
/// <param name="IsPassword">Das fokussierte Element ist ein Passwortfeld: nichts gelesen (W2-FIX V-3).</param>
public sealed record UiaSelection(bool HasTextPattern, string Text, bool IsPassword = false);

/// <summary>Markierung des fokussierten Elements; Windows: <c>UiaSelectionSource</c>, Tests: Fake.</summary>
public interface ISelectionTextSource
{
    /// <returns>null = kein Element mit Fokus.</returns>
    UiaSelection? Selection();
}

public enum SelectionReadKind
{
    Text,
    Empty,
    Password,
    Failed,
}

/// <summary>Ergebnis der Erfassung. <paramref name="ToastKey"/> nennt bei <see cref="SelectionReadKind.Failed"/> den Grund.</summary>
/// <param name="ElementId">Fokussiertes Element beim Erfassen (UIA-RuntimeId); das Ersetzen geht nur dorthin (W2-FIX M-4).</param>
public sealed record SelectionRead(SelectionReadKind Kind, string Text = "", string? ToastKey = null, bool ViaClipboard = false,
    string? ElementId = null)
{
    public static SelectionRead Empty { get; } = new(SelectionReadKind.Empty);

    public static SelectionRead Password { get; } = new(SelectionReadKind.Password);
}

/// <summary>
/// Holt die Markierung des Zielfensters (W2-SEL): zuerst Passwortfeld ausschließen (UI Automation mit MSAA-Rückfall), dann UI
/// Automation TextPattern (liest, ohne die Zwischenablage anzufassen), ohne TextPattern Rückfall auf simuliertes Strg+C bzw.
/// Strg+Einfg mit Sicherung und Wiederherstellung aller sicherbaren Formate (<see cref="SelectionCapture"/>). Meldet TextPattern
/// eine leere Markierung, gilt sie als leer – kein Strg+C, das in manchen Editoren ohne Markierung die ganze Zeile kopiert.
/// UIA-Abfragen laufen auf einem Pool-Thread mit Zeitgrenze (wie <see cref="FocusProbe"/>).
/// </summary>
public sealed class SelectionReader(
    IFocusProbe focus,
    ISelectionTextSource? uia,
    SelectionCapture capture,
    IWindowAdapter windows,
    TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(600);

    /// <param name="target">Fenster, in dem der Nutzer die Markierung gesetzt hat (muss vorn sein).</param>
    /// <param name="console">Strg+Einfg statt Strg+C beim Rückfall (<see cref="SelectionCapture.IsConsole"/>).</param>
    public async Task<SelectionRead> ReadAsync(IntPtr target, bool console, CancellationToken ct)
    {
        if (windows.Foreground() != target)
        {
            return new SelectionRead(SelectionReadKind.Failed, ToastKey: "Rewrite_TargetChanged");
        }
        if (windows.IsProtected(target))
        {
            return new SelectionRead(SelectionReadKind.Failed, ToastKey: "Rewrite_TargetProtected");
        }
        if (await focus.ProbeAsync(ct).ConfigureAwait(false) == FocusKind.Password)
        {
            return SelectionRead.Password;
        }
        // W2-FIX M-4: Element merken, das ersetzt werden soll (vor einem Strg+C, das den Fokus nicht ändert).
        var element = await focus.FocusedElementAsync(ct).ConfigureAwait(false);
        var fromUia = await QueryAsync(ct).ConfigureAwait(false);
        if (fromUia is { IsPassword: true })
        {
            return SelectionRead.Password;
        }
        if (fromUia is { HasTextPattern: true })
        {
            return string.IsNullOrWhiteSpace(fromUia.Text) ? SelectionRead.Empty : new SelectionRead(SelectionReadKind.Text, fromUia.Text, ElementId: element);
        }
        Capture copied;
        try
        {
            copied = await capture.CaptureAsync(target, console, ct).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return new SelectionRead(SelectionReadKind.Failed, ToastKey: "Selection_ClipboardBusy");
        }
        if (copied.Text is { } text)
        {
            return new SelectionRead(SelectionReadKind.Text, text, ViaClipboard: true, ElementId: element);
        }
        return copied.ToastKey == Capture.Nothing.ToastKey ? SelectionRead.Empty : new SelectionRead(SelectionReadKind.Failed, ToastKey: copied.ToastKey);
    }

    /// <summary>
    /// Für den schwebenden Knopf, ohne die Zwischenablage anzufassen: nie im Passwortfeld; mit TextPattern nur bei einer nicht
    /// leeren Markierung (nicht nur Leerzeichen); ohne TextPattern (keine Auskunft) nur, wenn die Geste mit dem Textzeiger begann –
    /// ein Klick erfasst dann per Strg+C. Früher hieß „keine Auskunft“ immer Knopf (TP-WBUTTON: Fenster ziehen, Explorer).
    /// </summary>
    /// <param name="textCursor">Mauszeiger beim Drücken war der Textzeiger (I-Beam).</param>
    public async Task<bool> MaySelectAsync(bool textCursor, CancellationToken ct)
    {
        if (await focus.ProbeAsync(ct).ConfigureAwait(false) == FocusKind.Password)
        {
            return false;
        }
        var fromUia = await QueryAsync(ct).ConfigureAwait(false);
        return fromUia switch
        {
            { IsPassword: true } => false,
            { HasTextPattern: true } => !string.IsNullOrWhiteSpace(fromUia.Text),
            _ => textCursor,
        };
    }

    private async Task<UiaSelection?> QueryAsync(CancellationToken ct)
    {
        if (uia is null)
        {
            return null;
        }
        try
        {
            return await Task.Run(uia.Selection, ct).WaitAsync(timeout ?? DefaultTimeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
