using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Vulkan-Sperre der Begleitprozesse (<see cref="LocalStt"/>, <see cref="BundledTextEngine"/>; TS-WBERICHT Hinweis 7):
/// Startet die Vulkan-Variante nicht (<see cref="SidecarStartException"/>) oder scheitert ihr Download so, dass ein neuer
/// Versuch nicht hilft (<see cref="DownloadException.Retryable"/> = false: Prüfsumme, Manifest, Archiv ohne Exe, Platz),
/// rechnet die Sitzung auf der CPU. Ein Netz-, Datei- oder wiederholbarer Download-Fehler sperrt nur
/// <see cref="RetryAfter"/> lang: ein kurzer Netzfehler schaltet die GPU nicht bis zum App-Neustart ab, und offline kostet
/// trotzdem nicht jedes Diktat einen neuen Download-Versuch.
/// </summary>
/// <remarks>Monotone Zeit (<see cref="TimeProvider.GetTimestamp"/>): ein Rücksprung der Uhr verlängert die Sperre nicht.</remarks>
internal sealed class VulkanBlock(TimeProvider time)
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private bool _session;
    private long? _since;

    public bool Active => _session || (_since is { } since && time.GetElapsedTime(since) < RetryAfter);

    public void Failed(Exception e)
    {
        if (e is SidecarStartException or DownloadException { Retryable: false })
        {
            _session = true;
        }
        else
        {
            _since = time.GetTimestamp();
        }
    }
}
