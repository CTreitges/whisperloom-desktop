namespace WhisperLoom.App.Updates;

/// <summary>Update-Kanal (W3-CHAN). Gespeichert als <c>desktop.updateChannel</c> = <c>stable</c>/<c>beta</c>/<c>test</c>.</summary>
public enum UpdateChannel
{
    Stable,
    Beta,
    Test,
}

/// <summary>
/// Abbildung Kanal × Betriebssystem → Velopack-Kanal (= <c>vpk pack --channel</c>, Datei <c>releases.&lt;kanal&gt;.json</c>).
/// Stabil bleibt der OS-Name (<c>win</c>/<c>linux</c>) wie bei 1.0.0, das ohne <c>--channel</c> gepackt wurde; sonst
/// fänden installierte 1.0.0 keine Updates mehr. Jedes OS braucht einen eigenen Feature-Kanal.
/// </summary>
// Doku: Context7 /velopack/velopack.docs, packaging/channels (abgerufen 2026-11-02).
public static class UpdateChannels
{
    public static readonly IReadOnlyList<UpdateChannel> All = [UpdateChannel.Stable, UpdateChannel.Beta, UpdateChannel.Test];

    /// <summary>Velopack-Kurzname des laufenden OS.</summary>
    public static string CurrentOs => OperatingSystem.IsWindows() ? "win" : "linux";

    public static string Key(this UpdateChannel channel) => channel switch
    {
        UpdateChannel.Beta => "beta",
        UpdateChannel.Test => "test",
        _ => "stable",
    };

    /// <summary>Unbekannt oder leer → Stabil (sicherer Standard).</summary>
    public static UpdateChannel Parse(string? key) =>
        All.FirstOrDefault(c => string.Equals(c.Key(), key, StringComparison.OrdinalIgnoreCase));

    /// <param name="os"><c>win</c> oder <c>linux</c>.</param>
    public static string FeedName(this UpdateChannel channel, string os) =>
        channel == UpdateChannel.Stable ? os : $"{os}-{channel.Key()}";

    /// <summary>Umkehrung von <see cref="FeedName"/>; null bei fremdem Kanal.</summary>
    public static UpdateChannel? FromFeedName(string? feed, string os) =>
        All.Cast<UpdateChannel?>().FirstOrDefault(c => string.Equals(c!.Value.FeedName(os), feed, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Kanäle der automatischen Prüfung: immer Stabil; der gewählte Vorab-Kanal nur mit „Vorabversionen automatisch
    /// installieren“. Ohne den Schalter bietet die Prüfung auch im Kanal Beta/Test nur stabile Versionen an. Die Kanäle
    /// sind geschachtelt (Test ⊇ Beta ⊇ Stabil, TP-WUPDATE): Test bietet auch Betas an, sonst verpasste ein Test-Nutzer
    /// eine Beta, die neuer ist als sein letzter Test-Build.
    /// </summary>
    public static IReadOnlyList<UpdateChannel> AutoChannels(UpdateChannel chosen, bool prereleaseAuto) =>
        !prereleaseAuto ? [UpdateChannel.Stable] : chosen switch
        {
            UpdateChannel.Beta => [UpdateChannel.Stable, UpdateChannel.Beta],
            UpdateChannel.Test => [UpdateChannel.Stable, UpdateChannel.Beta, UpdateChannel.Test],
            _ => [UpdateChannel.Stable],
        };
}
