using Avalonia.Headless.XUnit;
using WhisperLoom.App.Services;
using WhisperLoom.App.Tests.Settings;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>
/// TP-WUPDATE: Sofortprüfung nach geändertem Kanal oder Vorab-Schalter (entprellt, nie parallel, Richtlinie gilt) und
/// Kanal Test schließt Betas ein. Nur gegen <see cref="FakeUpdateClient"/>.
/// </summary>
public sealed class UpdateRequestTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-update-req-" + Guid.NewGuid().ToString("N"));
    private readonly AppLog _log;

    /// <summary>Gibt die Entprell-Wartezeit erst frei, wenn der Test es sagt.</summary>
    private readonly TaskCompletionSource _debounce = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _debounceWaits;

    public UpdateRequestTest() => _log = new AppLog(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private Task Delay(TimeSpan span, CancellationToken ct)
    {
        if (span == UpdateService.RequestDelay)
        {
            Interlocked.Increment(ref _debounceWaits);
            return _debounce.Task.WaitAsync(ct);
        }
        return Task.Delay(Timeout.Infinite, ct); // tägliche Runde: im Test nie
    }

    private UpdateService Service(FakeUpdateClient client, Func<PolicySettings>? policy = null, Func<string, Task<bool>>? confirm = null,
        UpdatePreferences? prefs = null) =>
        new(client, _log, policy ?? (() => PolicySettings.None), confirm ?? (_ => Task.FromResult(false)), Delay, () => prefs ?? UpdatePreferences.Default);

    [Fact]
    public async Task Schnelles_Mehrfachumschalten_prueft_genau_einmal()
    {
        var client = new FakeUpdateClient();
        var service = Service(client);
        var requests = new[] { service.RequestCheckAsync(), service.RequestCheckAsync(), service.RequestCheckAsync() };
        Assert.Equal(0, client.Checks); // erst nach der Wartezeit
        _debounce.SetResult();
        await Task.WhenAll(requests);
        Assert.Equal(1, client.Checks);
        Assert.Equal(1, CountInLog("update.request reason=settings"));
    }

    [Fact]
    public async Task Einzelne_Aenderung_prueft_einmal_und_fragt_vor_der_Installation()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        var asked = new List<string>();
        var service = Service(client, confirm: v =>
        {
            asked.Add(v);
            return Task.FromResult(false);
        });
        _debounce.SetResult();
        await service.RequestCheckAsync();
        Assert.Equal(1, client.Checks);
        Assert.Equal(["0.9.1"], asked);
        Assert.Equal(0, client.Downloads); // abgelehnt → nichts geladen
    }

    [Fact]
    public async Task Sofortpruefung_laeuft_nie_parallel_zur_laufenden_Runde()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int running = 0, maxRunning = 0, asked = 0;
        var service = Service(client, confirm: async _ =>
        {
            var now = Interlocked.Increment(ref running);
            maxRunning = Math.Max(maxRunning, now);
            if (Interlocked.Increment(ref asked) == 1)
            {
                firstAsked.SetResult();
                await release.Task; // Startrunde hängt in der Nachfrage
            }
            Interlocked.Decrement(ref running);
            return false;
        });
        using var cts = new CancellationTokenSource();
        var loop = Task.Run(() => service.RunAsync(cts.Token));
        await firstAsked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var request = service.RequestCheckAsync();
        _debounce.SetResult();
        await Task.Delay(100);
        Assert.False(request.IsCompleted); // wartet auf die laufende Runde
        Assert.Equal(1, client.Checks);

        release.SetResult(false);
        await request.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((2, 2, 1), (client.Checks, asked, maxRunning));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task Richtlinie_aus_prueft_auch_nach_Kanalwechsel_nicht()
    {
        var client = new FakeUpdateClient { Available = "0.9.1" };
        var service = Service(client, () => new PolicySettings { UpdatesEnabled = false });
        _debounce.SetResult();
        await service.RequestCheckAsync();
        Assert.Equal((0, 0), (client.Checks, _debounceWaits));
    }

    [Fact]
    public async Task Richtlinie_waehrend_der_Wartezeit_ausgeschaltet_prueft_nicht()
    {
        var policy = PolicySettings.None;
        var client = new FakeUpdateClient { Available = "0.9.1" };
        var service = Service(client, () => policy);
        var request = service.RequestCheckAsync();
        policy = new PolicySettings { UpdatesEnabled = false };
        _debounce.SetResult();
        await request;
        Assert.Equal(0, client.Checks);
        Assert.Contains("update.check status=disabled-by-policy", LogText());
    }

    [Fact]
    public async Task Kanal_Test_bietet_neuere_Beta_an()
    {
        // Gefunden 2026-11-05: Kanal Test + Vorab-Schalter, 1.2.0-beta.1 veröffentlicht, Prüfung meldete „current“.
        var client = new FakeUpdateClient { CurrentVersion = "1.1.0" };
        client.Newer[FakeUpdateClient.Beta] = "1.2.0-beta.1";
        var asked = new List<string>();
        var service = Service(client, confirm: v =>
        {
            asked.Add(v);
            return Task.FromResult(false);
        }, prefs: new UpdatePreferences(UpdateChannel.Test, PrereleaseAuto: true));
        Assert.Equal(UpdateOutcome.Declined, await service.CheckOnceAsync(CancellationToken.None));
        Assert.Equal(["1.2.0-beta.1"], asked);
        Assert.Equal([FakeUpdateClient.Stable, FakeUpdateClient.Beta, FakeUpdateClient.Test], client.Checked);
    }

    private string LogText() => File.Exists(_log.FilePath) ? File.ReadAllText(_log.FilePath) : "";

    private int CountInLog(string text) => LogText().Split('\n').Count(l => l.Contains(text, StringComparison.Ordinal));
}

/// <summary>TP-WUPDATE, Seite „System“: Kanal und Vorab-Schalter melden nach dem Speichern genau eine Prüfanfrage.</summary>
public sealed class UpdateRequestPageTest
{
    private static async Task<int> CountRequests(ControllerFixture f, Action<SystemPageViewModel> change, Action? onRequest = null)
    {
        var count = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        UpdateCheckRequests.Handler = () =>
        {
            onRequest?.Invoke();
            Interlocked.Increment(ref count);
            done.TrySetResult();
        };
        try
        {
            var vm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()) { AutoSave = true };
            var page = new SystemPageViewModel(vm);
            change(page);
            await vm.Pending;
            await Task.WhenAny(done.Task, Task.Delay(500));
            return count;
        }
        finally
        {
            UpdateCheckRequests.Handler = null;
        }
    }

    [AvaloniaFact]
    public async Task Kanalwechsel_meldet_eine_Pruefanfrage_nach_dem_Speichern()
    {
        using var f = new ControllerFixture();
        string? seen = null;
        var count = await CountRequests(f, page => page.UpdateChannel = page.UpdateChannels[2], () => seen = SettingsStore.Desktop(f.Controller.Settings).UpdateChannel);
        Assert.Equal(1, count);
        Assert.Equal("test", seen); // die Prüfung liest schon den neuen Kanal
    }

    [AvaloniaFact]
    public async Task Vorab_Schalter_meldet_eine_Pruefanfrage()
    {
        var settings = new Core.Contracts.Settings.WhisperLoomSettings();
        SettingsStore.SetDesktop(settings, new DesktopSettings { UpdateChannel = "beta" });
        using var f = new ControllerFixture(settings);
        Assert.Equal(1, await CountRequests(f, page => page.UpdatePrereleaseAuto = true));
    }

    [AvaloniaFact]
    public async Task Gleicher_Kanal_oder_Richtlinie_aus_meldet_nichts()
    {
        using var f = new ControllerFixture();
        Assert.Equal(0, await CountRequests(f, page => page.UpdateChannel = page.UpdateChannels[0]));

        using var locked = new ControllerFixture();
        locked.Controller.Policy = new PolicySettings { UpdatesEnabled = false };
        Assert.Equal(0, await CountRequests(locked, page => page.UpdateChannel = page.UpdateChannels[2]));
    }
}
