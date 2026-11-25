namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Vulkan-Sperre der Begleitprozesse (<see cref="LocalStt"/>, <see cref="BundledTextEngine"/>; TS-WBERICHT Hinweis 7):
/// Startet die Vulkan-Variante nicht (<see cref="SidecarStartException"/>), rechnet die Sitzung auf der CPU. Ein Download-,
/// Netz- oder Dateifehler sperrt nur <see cref="RetryAfter"/> lang: ein kurzer Netzfehler schaltet die GPU nicht bis zum
/// App-Neustart ab, und offline kostet trotzdem nicht jedes Diktat einen neuen Download-Versuch.
/// </summary>
internal sealed class VulkanBlock(Func<DateTimeOffset> now)
{
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(10);

    private bool _startFailed;
    private DateTimeOffset? _until;

    public bool Active => _startFailed || (_until is { } until && now() < until);

    public void Failed(Exception e)
    {
        if (e is SidecarStartException)
        {
            _startFailed = true;
        }
        else
        {
            _until = now() + RetryAfter;
        }
    }
}
