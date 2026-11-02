using WhisperLoom.App.Updates;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>Fake statt Velopack: neuere Version je Feed-Kanal, Versionsliste je Kanal, zählt Aufrufe.</summary>
internal sealed class FakeUpdateClient : IUpdateClient
{
    public static string Stable => UpdateChannel.Stable.FeedName(UpdateChannels.CurrentOs);

    public static string Beta => UpdateChannel.Beta.FeedName(UpdateChannels.CurrentOs);

    public static string Test => UpdateChannel.Test.FeedName(UpdateChannels.CurrentOs);

    public bool IsInstalled { get; set; } = true;
    public string? CurrentVersion { get; set; } = "0.9.0";

    /// <summary>Neuere Version je Feed-Kanal (Ergebnis von <see cref="CheckAsync"/>).</summary>
    public Dictionary<string, string> Newer { get; } = [];

    /// <summary>Neuere Version im stabilen Kanal (Kurzform der bisherigen Tests).</summary>
    public string? Available
    {
        get => Newer.GetValueOrDefault(Stable);
        set
        {
            if (value is null) Newer.Remove(Stable);
            else Newer[Stable] = value;
        }
    }

    /// <summary>Alle Versionen je Feed-Kanal (Ergebnis von <see cref="ListAsync"/>).</summary>
    public Dictionary<string, string[]> Feeds { get; } = [];

    public Exception? CheckError { get; set; }
    public Exception? ListError { get; set; }
    public List<string> Checked { get; } = [];
    public List<(string Version, string Channel, bool Downgrade)> Downloaded { get; } = [];
    public List<string> Applied { get; } = [];
    public int Checks => Checked.Count;
    public int Downloads => Downloaded.Count;
    public int Restarts { get; private set; }
    public int Exits { get; private set; }

    public Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct)
    {
        Checked.Add(channel);
        if (CheckError is not null) return Task.FromException<UpdateRelease?>(CheckError);
        return Task.FromResult(Newer.TryGetValue(channel, out var v) ? new UpdateRelease(v, channel, null) : null);
    }

    public Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct)
    {
        if (ListError is not null) return Task.FromException<IReadOnlyList<UpdateRelease>>(ListError);
        IReadOnlyList<UpdateRelease> list = [.. Feeds.GetValueOrDefault(channel, []).Select(v => new UpdateRelease(v, channel, new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero)))];
        return Task.FromResult(list);
    }

    public Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct)
    {
        Downloaded.Add((release.Version, release.Channel, downgrade));
        return Task.CompletedTask;
    }

    public void ApplyAndRestart(UpdateRelease release)
    {
        Applied.Add(release.Version);
        Restarts++;
    }

    public void ApplyAndExit(UpdateRelease release)
    {
        Applied.Add(release.Version);
        Exits++;
    }
}
