namespace WhisperLoom.App.Platform.Windows.TextOutput;

/// <summary>Gesicherter Inhalt der Zwischenablage (alle speicherbaren Formate). Leer = Zwischenablage war leer.</summary>
public sealed record ClipboardSnapshot(IReadOnlyList<(uint Format, byte[] Data)> Items);

/// <summary>Zwischenablage. Win32: <see cref="Win32TextClipboard"/>; Tests: Fake.</summary>
public interface IClipboardAdapter
{
    /// <summary>Zähler, der sich bei jeder Änderung der Zwischenablage erhöht.</summary>
    uint SequenceNumber { get; }

    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    ClipboardSnapshot Save();

    /// <param name="private">Aus Zwischenablage-Verlauf und Cloud-Sync ausschließen (Formate ExcludeClipboardContentFromMonitorProcessing,
    /// CanIncludeInClipboardHistory=0, CanUploadToCloudClipboard=0).</param>
    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    void SetText(string text, bool @private);

    /// <summary>Stellt eine Sicherung wieder her (privat, damit der Verlauf keinen Doppel-Eintrag bekommt).</summary>
    /// <exception cref="IOException">Zwischenablage belegt.</exception>
    void Restore(ClipboardSnapshot snapshot);
}

/// <summary>Tastatur-Ausgabe per SendInput; alle Ereignisse tragen die eigene dwExtraInfo-Kennung.</summary>
public interface IKeySender
{
    /// <summary>Wartet, bis Strg, Alt, Umschalt und Win losgelassen sind (höchstens <paramref name="max"/>).</summary>
    Task WaitForModifiersReleasedAsync(TimeSpan max, CancellationToken ct);

    /// <returns>false, wenn das System die Eingabe nicht angenommen hat.</returns>
    bool SendCtrlV();

    bool SendEnter();

    /// <summary>Tippt den Text als Unicode (KEYEVENTF_UNICODE); Zeilenumbrüche als Umschalt+Enter.</summary>
    bool Type(string text);
}

/// <summary>Fenster-Abfragen für das Ziel des Einfügens.</summary>
public interface IWindowAdapter
{
    IntPtr Foreground();

    bool Exists(IntPtr window);

    /// <summary>Gehört das Fenster einem Prozess mit höherer Integritätsstufe (Admin), den UIPI vor SendInput schützt?</summary>
    bool IsProtected(IntPtr window);

    /// <summary>Holt das Fenster nach vorn; true, wenn es danach im Vordergrund ist.</summary>
    bool Activate(IntPtr window);
}
