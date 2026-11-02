using WhisperLoom.App.Services;

namespace WhisperLoom.App.Updates;

/// <summary>Update-Einstellungen des Nutzers (<c>desktop.updateChannel</c>, <c>desktop.updatePrereleaseAuto</c>).</summary>
public sealed record UpdatePreferences(UpdateChannel Channel, bool PrereleaseAuto)
{
    public static readonly UpdatePreferences Default = new(UpdateChannel.Stable, false);

    public static UpdatePreferences From(DesktopSettings desktop) =>
        new(UpdateChannels.Parse(desktop.UpdateChannel), desktop.UpdatePrereleaseAuto);

    /// <summary>
    /// Fragt die Kanäle nacheinander und liefert die höchste neuere Version (null = aktuell). Mehrere Kanäle, weil ein
    /// Beta-Nutzer auch den späteren stabilen Release bekommen soll; jeder Feed enthält nur seinen Kanal.
    /// </summary>
    internal static async Task<UpdateRelease?> NewestAsync(IUpdateClient client, IEnumerable<UpdateChannel> channels, string os, CancellationToken ct)
    {
        UpdateRelease? best = null;
        foreach (var channel in channels)
        {
            var found = await client.CheckAsync(channel.FeedName(os), ct);
            if (found is not null && (best is null || AppVersion.Compare(found.Version, best.Version) > 0))
            {
                best = found;
            }
        }
        return best;
    }
}
