using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>UX-6 N19: Laden einer Version zeigt Prozent und Restzeit und lässt sich abbrechen (Richtlinie 03 §3.2, &gt; 10 s).</summary>
public sealed class VersionDownloadTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-download-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static FakeUpdateClient Client()
    {
        Loc.Instance.SetLanguage("de");
        var client = new FakeUpdateClient { CurrentVersion = "1.0.0" };
        client.Feeds[FakeUpdateClient.Stable] = ["1.0.0", "1.0.1"];
        return client;
    }

    private VersionListViewModel Vm(IUpdateClient client) =>
        new(client, new AppLog(_dir), () => PolicySettings.None, (_, _) => Task.FromResult(true));

    /// <summary>Meldet Fortschritt und wartet dann, bis der Test freigibt oder abbricht.</summary>
    private sealed class WaitingClient(FakeUpdateClient inner) : IUpdateClient
    {
        public TaskCompletionSource Release { get; } = new();
        public bool IsInstalled => inner.IsInstalled;
        public string? CurrentVersion => inner.CurrentVersion;
        public Task<UpdateRelease?> CheckAsync(string channel, CancellationToken ct) => inner.CheckAsync(channel, ct);
        public Task<IReadOnlyList<UpdateRelease>> ListAsync(string channel, CancellationToken ct) => inner.ListAsync(channel, ct);

        public async Task DownloadAsync(UpdateRelease release, bool downgrade, CancellationToken ct, Action<int>? progress = null)
        {
            await inner.DownloadAsync(release, downgrade, ct, progress);
            progress?.Invoke(42);
            await Release.Task.WaitAsync(ct);
        }

        public void ApplyAndRestart(UpdateRelease release) => inner.ApplyAndRestart(release);
        public void ApplyAndExit(UpdateRelease release) => inner.ApplyAndExit(release);
    }

    [AvaloniaFact]
    public async Task Download_zeigt_Prozent_und_endet_mit_Neustart()
    {
        var inner = Client();
        var client = new WaitingClient(inner);
        var vm = Vm(client);
        await vm.LoadAsync();
        var install = vm.InstallAsync(vm.Rows[0]);
        Assert.True(vm.IsDownloading);
        Assert.Equal(42, vm.Progress);
        Assert.Contains("42 %", vm.Status, StringComparison.Ordinal);
        Assert.StartsWith("Version 1.0.1 wird geladen", vm.Status, StringComparison.Ordinal);

        client.Release.SetResult();
        Assert.Equal(UpdateOutcome.Applied, await install);
        Assert.False(vm.IsDownloading);
        Assert.Equal(["1.0.1"], inner.Applied);
    }

    [AvaloniaFact]
    public async Task Abbrechen_stoppt_den_Download_ohne_Neustart()
    {
        var inner = Client();
        var client = new WaitingClient(inner);
        var vm = Vm(client);
        await vm.LoadAsync();
        var install = vm.InstallAsync(vm.Rows[0]);
        Assert.True(vm.CancelDownloadCommand.CanExecute(null));

        vm.CancelDownloadCommand.Execute(null);
        Assert.Equal(UpdateOutcome.Declined, await install);
        Assert.False(vm.IsDownloading);
        Assert.False(vm.IsBusy);
        Assert.Empty(inner.Applied);
        Assert.Equal(Loc.Instance["Channel_DownloadCanceled"], vm.Status);
    }

    [AvaloniaTheory]
    [InlineData(0, 10, "0 %")]
    [InlineData(4, 10, "4 %")] // unter 5 % noch keine Schätzung
    [InlineData(50, 1, "50 %")] // unter 2 s noch keine Schätzung
    [InlineData(50, 30, "50 % · noch weniger als 1 Min.")]
    [InlineData(25, 60, "25 % · noch etwa 3 Min.")]
    [InlineData(100, 60, "100 %")]
    public void Prozent_mit_Restzeit(int percent, int seconds, string expected)
    {
        Loc.Instance.SetLanguage("de");
        Assert.Equal(expected, DownloadText.OfPercent(percent, TimeSpan.FromSeconds(seconds)));
    }
}
