namespace WhisperLoom.App.Updates;

/// <summary>
/// Schmale Sicht auf Velopacks <c>UpdateManager</c>: so sind <see cref="UpdateService"/> und
/// <see cref="UpdateCommand"/> ohne echte Installation testbar.
/// </summary>
public interface IUpdateClient
{
    /// <summary>Über Setup.exe bzw. als AppImage installiert; nur dann gibt es Updates.</summary>
    bool IsInstalled { get; }

    /// <summary>Installierte Version laut Velopack, sonst null.</summary>
    string? CurrentVersion { get; }

    /// <summary>Fragt den Feed; liefert die neuere Version oder null (aktuell).</summary>
    Task<string?> CheckAsync(CancellationToken ct);

    /// <summary>Lädt die zuletzt bei <see cref="CheckAsync"/> gefundene Version.</summary>
    Task DownloadAsync(CancellationToken ct);

    /// <summary>Installiert das geladene Update und startet die App neu; beendet den Prozess.</summary>
    void ApplyAndRestart();

    /// <summary>Installiert das geladene Update ohne Neustart; beendet den Prozess (CLI <c>update</c>).</summary>
    void ApplyAndExit();
}
