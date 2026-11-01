using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Platform.Windows.Input;

/// <summary>Eigenschaften des fokussierten UI-Automation-Elements (nur Metadaten, nie Inhalt).</summary>
/// <param name="ControlType">UIA_*ControlTypeId, z. B. 50004 = Edit.</param>
/// <param name="ValueAvailable">ValuePattern vorhanden.</param>
/// <param name="ValueReadOnly">ValuePattern meldet „nur lesen“.</param>
/// <param name="ProcessId">Prozess des Elements (0 = unbekannt).</param>
public sealed record FocusInfo(int ControlType, bool IsPassword, bool ValueAvailable, bool ValueReadOnly, int ProcessId = 0);

/// <summary>Quelle des fokussierten Elements. Windows: <see cref="UiaFocusSource"/>; Tests: Fake.</summary>
public interface IFocusSource
{
    /// <returns>null = kein Element mit Fokus.</returns>
    FocusInfo? Focused();
}

/// <summary>Fragt die Fokus-Klasse für die Ausgabe-Entscheidung ab (<see cref="OutputDecision"/>).</summary>
public interface IFocusProbe
{
    Task<FocusKind> ProbeAsync(CancellationToken ct);
}

/// <summary>Linux und Tests ohne Fokus-Abfrage: immer unbekannt.</summary>
public sealed class UnknownFocusProbe : IFocusProbe
{
    public static readonly UnknownFocusProbe Instance = new();

    public Task<FocusKind> ProbeAsync(CancellationToken ct) => Task.FromResult(FocusKind.Unknown);
}

/// <summary>
/// Fokus-Klasse aus UI Automation. Eine Abfrage darf das Einfügen nicht aufhalten: sie läuft auf einem Pool-Thread (MTA, wie
/// von UIA für Clients empfohlen) mit <see cref="Timeout"/>; Zeitgrenze, COM-Fehler oder kein Element = unbekannt.
/// Bleibt UIA bei „unbekannt“, fragt <paramref name="fallback"/> (MSAA; Browser-Inhalte, Passwortfelder im Web).
/// </summary>
// Doku: learn.microsoft.com/windows/win32/winauto/uiauto-threading (Clients nicht auf dem UI-Thread) und
// learn.microsoft.com/windows/win32/winauto/uiauto-controltype-ids (abgerufen 2026-11-01).
public sealed class FocusProbe(IFocusSource source, TimeSpan? timeout = null, IFocusSource? fallback = null) : IFocusProbe
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(600);

    public TimeSpan Timeout { get; } = timeout ?? DefaultTimeout;

    /// <summary>Letzte Abfrage (für <c>status --json</c>): Klasse und Control-Type, nie Inhalt.</summary>
    public (FocusKind Kind, int ControlType)? Last { get; private set; }

    public async Task<FocusKind> ProbeAsync(CancellationToken ct)
    {
        FocusInfo? info;
        try
        {
            info = await Task.Run(() =>
            {
                var primary = Query(source);
                return Classify(primary) == FocusKind.Unknown && fallback is not null ? Query(fallback) ?? primary : primary;
            }, ct).WaitAsync(Timeout, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is TimeoutException or System.Runtime.InteropServices.COMException or InvalidCastException
                                      or UnauthorizedAccessException or InvalidOperationException)
        {
            info = null;
        }
        var kind = Classify(info);
        Last = (kind, info?.ControlType ?? 0);
        return kind;
    }

    /// <summary>MSAA-Rolle/Zustand → UIA-Begriffe für <see cref="FocusProbe.Classify"/>.</summary>
    public static FocusInfo? FromMsaa(int role, int state)
    {
        var password = (state & 0x20000000) != 0;
        var readOnly = (state & 0x40) != 0;
        return role switch
        {
            0x2A => new FocusInfo(50004, password, true, readOnly), // ROLE_SYSTEM_TEXT → Edit
            0x2B or 0x2C or 0x2D or 0x39 => new FocusInfo(50000, password, false, false), // Push-/Check-/Radio-/Menu-Button
            0x22 or 0x21 or 0x23 or 0x24 => new FocusInfo(50007, password, false, false), // ListItem, List, Outline
            _ when password => new FocusInfo(50004, true, true, false),
            _ => null,
        };
    }

    /// <summary>Eine Quelle fragen; COM-Fehler einer Quelle = keine Auskunft (die andere darf noch antworten).</summary>
    private static FocusInfo? Query(IFocusSource s)
    {
        try
        {
            return s.Focused();
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidCastException or UnauthorizedAccessException
                                      or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    // UIA_*ControlTypeId (uiautomationclient.h).
    private const int Edit = 50004;
    private const int Document = 50030;

    /// <summary>Steuerelemente, die nie Text annehmen. Bewusst ohne Text (50020), Pane, Custom, Window: Terminals und eigene
    /// Oberflächen (Electron-Canvas, Spiele) melden sich so und nehmen trotzdem Tastatureingaben an – dort gilt „unbekannt“.</summary>
    private static readonly HashSet<int> NeverText =
    [
        50000, // Button
        50002, // CheckBox
        50005, // Hyperlink
        50006, // Image
        50007, // ListItem
        50008, // List
        50009, // Menu
        50010, // MenuBar
        50011, // MenuItem
        50012, // ProgressBar
        50013, // RadioButton
        50014, // ScrollBar
        50015, // Slider
        50017, // StatusBar
        50018, // Tab
        50019, // TabItem
        50021, // ToolBar
        50023, // Tree
        50024, // TreeItem
        50031, // SplitButton
        50034, // Header
        50035, // HeaderItem
        50037, // TitleBar
    ];

    /// <summary>Reine Zuordnung (Tabelle in docs/parity-ux.md).</summary>
    public static FocusKind Classify(FocusInfo? info)
    {
        if (info is null)
        {
            return FocusKind.Unknown;
        }
        if (info.IsPassword)
        {
            return FocusKind.Password;
        }
        if (info.ValueAvailable && info.ValueReadOnly)
        {
            return FocusKind.NotEditable;
        }
        if (info.ControlType == Edit || (info.ValueAvailable && info.ControlType != Document && !NeverText.Contains(info.ControlType)))
        {
            return FocusKind.Editable;
        }
        if (info.ControlType == Document && info.ValueAvailable)
        {
            return FocusKind.Editable;
        }
        return NeverText.Contains(info.ControlType) ? FocusKind.NotEditable : FocusKind.Unknown;
    }
}
