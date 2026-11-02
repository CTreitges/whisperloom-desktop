namespace WhisperLoom.App.Updates;

/// <summary>Eine Version in einem Feed-Kanal.</summary>
/// <param name="Channel">Velopack-Kanal (<c>win</c>, <c>win-beta</c>, …).</param>
/// <param name="Published">Datum des GitHub-Releases; null bei Ordner- oder URL-Feeds.</param>
/// <param name="Asset">Velopacks Paket-Eintrag; für Tests beliebig.</param>
public sealed record UpdateRelease(string Version, string Channel, DateTimeOffset? Published, object? Asset = null);

/// <summary>
/// Schmale Sicht auf Velopacks <c>UpdateManager</c>: so sind <see cref="UpdateService"/>, <see cref="UpdateCommand"/> und
/// die Versionsliste ohne echte Installation testbar.
/// </summary>
public interface IUpdateClient
{
    /// <summary>Über Setup.exe bzw. als AppImage installiert; nur dann gibt es Updates.</summary>
    bool IsInstalled { get; }

    /// <summary>Installierte Version laut Velopack, sonst null.</summary>
    string? CurrentVersion { get; }

    /// <summary>Fragt den Kanal; liefert die neueste Version, wenn sie höher als die installierte ist, sonst null.</summary>
    Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct);

    /// <summary>Alle vollen Pakete des Kanals (für die Versionsliste); leer, wenn der Kanal nichts hat.</summary>
    Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct);

    /// <summary>Lädt die Version; eine niedrigere oder gleiche Version (Downgrade, Kanalwechsel) nur mit <paramref name="downgrade"/>.</summary>
    Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct);

    /// <summary>Installiert die geladene Version und startet die App neu; beendet den Prozess.</summary>
    void ApplyAndRestart(UpdateRelease release);

    /// <summary>Installiert die geladene Version ohne Neustart; beendet den Prozess (CLI <c>update</c>).</summary>
    void ApplyAndExit(UpdateRelease release);
}
