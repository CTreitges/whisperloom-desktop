using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>W3-CHAN: Versionsliste über alle Kanäle, Installieren nur nach Rückfrage, Downgrade erkannt, Richtlinie sperrt.</summary>
public sealed class VersionListTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-versions-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string Version, bool Downgrade)> _asked = [];

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static FakeUpdateClient Client(string current)
    {
        // Texte auf Deutsch; Loc nur auf dem UI-Thread umschalten (AvaloniaFact).
        Loc.Instance.SetLanguage("de");
        var client = new FakeUpdateClient { CurrentVersion = current };
        client.Feeds[FakeUpdateClient.Stable] = ["1.0.0", "1.0.1"];
        client.Feeds[FakeUpdateClient.Beta] = ["1.1.0-beta.1", "1.1.0-beta.2"];
        client.Feeds[FakeUpdateClient.Test] = ["1.1.0-test.7.feat-x"];
        return client;
    }

    private readonly List<UpdateChannel> _chosen = [];

    private VersionListViewModel Vm(IUpdateClient client, bool answer = true, PolicySettings? policy = null) =>
        new(client, new AppLog(_dir), () => policy ?? PolicySettings.None, (v, down) =>
        {
            _asked.Add((v, down));
            return Task.FromResult(answer);
        }, channelChosen: c =>
        {
            _chosen.Add(c);
            return Task.CompletedTask;
        });

    [AvaloniaFact]
    public async Task Alle_Kanaele_neueste_zuerst_mit_installiert()
    {
        var vm = Vm(Client("1.1.0-beta.1"));
        await vm.LoadAsync();
        // SemVer: „test“ steht alphabetisch nach „beta“, also 1.1.0-test.* über 1.1.0-beta.*.
        Assert.Equal(["1.1.0-test.7.feat-x", "1.1.0-beta.2", "1.1.0-beta.1", "1.0.1", "1.0.0"], vm.Rows.Select(r => r.Version));
        Assert.Equal([UpdateChannel.Test, UpdateChannel.Beta, UpdateChannel.Beta, UpdateChannel.Stable, UpdateChannel.Stable], vm.Rows.Select(r => r.Channel));
        Assert.Equal(["1.1.0-beta.1"], vm.Rows.Where(r => r.IsInstalled).Select(r => r.Version));
        Assert.Equal(["1.0.1", "1.0.0"], vm.Rows.Where(r => r.IsDowngrade).Select(r => r.Version));
        Assert.Equal("Stabil", vm.Rows[^1].ChannelLabel);
        Assert.Equal("2. Nov. 2026", vm.Rows[0].Date); // UX-3: Datum in der Sprache
        Assert.True(vm.ShowBackToStable); // installiert ist eine Beta
        Assert.Equal("Zur neuesten stabilen Version (1.0.1)", vm.BackToStableText);
        Loc.Instance.SetLanguage("en");
        Assert.Equal("2 Nov 2026", vm.Rows[0].Date);
        Loc.Instance.SetLanguage("de");
        Assert.Equal("", vm.Status);
    }

    [AvaloniaFact]
    public async Task Hoehere_Version_nach_Rueckfrage_ohne_Downgrade()
    {
        var client = Client("1.0.1");
        var vm = Vm(client);
        await vm.LoadAsync();
        Assert.Equal(UpdateOutcome.Applied, await vm.InstallAsync(vm.Rows.First(r => r.Version == "1.1.0-beta.2")));
        Assert.Equal([("1.1.0-beta.2", false)], _asked);
        Assert.Equal([("1.1.0-beta.2", FakeUpdateClient.Beta, false)], client.Downloaded);
        Assert.Equal(1, client.Restarts);
    }

    [AvaloniaFact]
    public async Task Zurueck_zu_Stabil_ist_Downgrade_nur_nach_Rueckfrage()
    {
        var client = Client("1.1.0-beta.2");
        var vm = Vm(client, answer: false);
        await vm.LoadAsync();
        Assert.Equal(UpdateOutcome.Declined, await vm.BackToStableAsync());
        Assert.Equal([("1.0.1", true)], _asked);
        Assert.Empty(client.Downloaded);
        Assert.Equal(0, client.Restarts);
        Assert.Empty(_chosen);

        var yes = Vm(client, answer: true);
        await yes.LoadAsync();
        Assert.Equal(UpdateOutcome.Applied, await yes.BackToStableAsync());
        Assert.Equal([("1.0.1", FakeUpdateClient.Stable, true)], client.Downloaded);
        Assert.Equal(["1.0.1"], client.Applied);
        // Review W3-CHAN: Kanal geht mit zurück auf Stabil, sonst böte die Auto-Prüfung (Vorab-Schalter an) gleich wieder die Beta an.
        Assert.Equal([UpdateChannel.Stable], _chosen);
    }

    /// <summary>Wartet im Download, bis der Test ihn freigibt (zweiter Klick währenddessen).</summary>
    private sealed class SlowClient(FakeUpdateClient inner) : IUpdateClient
    {
        public TaskCompletionSource Release { get; } = new();
        public bool IsInstalled => inner.IsInstalled;
        public string? CurrentVersion => inner.CurrentVersion;
        public Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct) => inner.CheckAsync(channel, ct);
        public Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct) => inner.ListAsync(channel, ct);
        public async Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct, Action<int>? progress = null)
        {
            await inner.DownloadAsync(release, downgrade, ct, progress);
            await Release.Task.WaitAsync(ct);
        }
        public void ApplyAndRestart(UpdateRelease release) => inner.ApplyAndRestart(release);
        public void ApplyAndExit(UpdateRelease release) => inner.ApplyAndExit(release);
    }

    [AvaloniaFact]
    public async Task Zweiter_Klick_waehrend_Download_installiert_nicht_doppelt()
    {
        var inner = Client("1.0.0");
        var slow = new SlowClient(inner);
        var vm = Vm(slow);
        await vm.LoadAsync();
        var first = vm.InstallAsync(vm.Rows[0]);
        Assert.True(vm.IsBusy);
        // Der zweite Klick muss sofort zurückkommen, ohne Rückfrage und ohne zweiten Download.
        var second = vm.InstallAsync(vm.Rows[1]);
        var secondDone = second.IsCompleted;
        slow.Release.TrySetResult();
        Assert.True(secondDone, "zweiter Klick wartet auf einen Download");
        Assert.Equal(UpdateOutcome.Current, await second);
        Assert.Equal(UpdateOutcome.Applied, await first);
        Assert.Single(inner.Downloaded);
        Assert.Single(_asked);
        Assert.Equal([UpdateChannel.Test], _chosen);
    }

    [AvaloniaFact]
    public async Task Installierte_Version_wird_nicht_erneut_installiert()
    {
        var client = Client("1.0.1");
        var vm = Vm(client);
        await vm.LoadAsync();
        Assert.Equal(UpdateOutcome.Current, await vm.InstallAsync(vm.Rows.Single(r => r.IsInstalled)));
        Assert.Empty(_asked);
        Assert.False(vm.Rows.Single(r => r.IsInstalled).CanInstall);
    }

    [AvaloniaFact]
    public async Task Richtlinie_sperrt_Liste_und_Installation()
    {
        var client = Client("1.0.0");
        var policy = new PolicySettings { UpdatesEnabled = false };
        var vm = Vm(client, policy: policy);
        await vm.LoadAsync();
        Assert.Empty(vm.Rows);
        Assert.False(vm.Allowed);
        Assert.Contains("IT", vm.Status);
        var row = new VersionRow(UpdateChannel.Stable, new UpdateRelease("1.0.1", FakeUpdateClient.Stable, null), installed: false, downgrade: false);
        Assert.Equal(UpdateOutcome.DisabledByPolicy, await vm.InstallAsync(row));
        Assert.Empty(_asked);
        Assert.Empty(client.Downloaded);
    }

    [AvaloniaFact]
    public async Task Nicht_installiert_zeigt_Liste_installiert_aber_nicht()
    {
        var client = Client("1.0.0");
        client.IsInstalled = false;
        client.CurrentVersion = null;
        var vm = Vm(client);
        await vm.LoadAsync();
        Assert.Equal(5, vm.Rows.Count);
        Assert.Contains("installierte", vm.Status);
        Assert.Equal(UpdateOutcome.NotInstalled, await vm.InstallAsync(vm.Rows[0]));
        Assert.Empty(_asked);
    }

    [AvaloniaFact]
    public async Task Feed_Fehler_zeigt_Hinweis_ohne_Meldung_im_Log()
    {
        var log = new AppLog(_dir);
        var client = Client("1.0.0");
        client.ListError = new HttpRequestException("https://secret.invalid/x");
        var vm = new VersionListViewModel(client, log, () => PolicySettings.None, (_, _) => Task.FromResult(true));
        await vm.LoadAsync();
        Assert.Empty(vm.Rows);
        Assert.Contains("nicht geladen", vm.Status);
        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("update.list status=failed errorClass=HttpRequestException", text);
        Assert.DoesNotContain("secret", text);
    }

    [AvaloniaFact]
    public async Task Rueckweg_zu_Stabil_nur_von_einer_Vorabversion()
    {
        foreach (var stable in new[] { "1.0.1", "1.0.0" })
        {
            var vm = Vm(Client(stable));
            await vm.LoadAsync();
            Assert.False(vm.ShowBackToStable); // installiert ist schon stabil
        }
        var beta = Vm(Client("1.1.0-beta.2"));
        await beta.LoadAsync();
        Assert.True(beta.ShowBackToStable);
        Assert.Equal("1.0.1", beta.NewestStable!.Version);
    }
}
