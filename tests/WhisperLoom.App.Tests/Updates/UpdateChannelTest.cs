using WhisperLoom.App.Services;
using WhisperLoom.App.Updates;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>W3-CHAN: Kanal-Abbildung, Versionsvergleich, UpdateOptions und die Regel der automatischen Prüfung.</summary>
public sealed class UpdateChannelTest : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wl-channel-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(UpdateChannel.Stable, "win", "win")]
    [InlineData(UpdateChannel.Stable, "linux", "linux")]
    [InlineData(UpdateChannel.Beta, "win", "win-beta")]
    [InlineData(UpdateChannel.Beta, "linux", "linux-beta")]
    [InlineData(UpdateChannel.Test, "win", "win-test")]
    [InlineData(UpdateChannel.Test, "linux", "linux-test")]
    public void Kanal_je_OS(UpdateChannel channel, string os, string feed)
    {
        // Stabil bleibt der OS-Name wie bei 1.0.0 (ohne --channel gepackt), sonst fänden installierte 1.0.0 nichts mehr.
        Assert.Equal(feed, channel.FeedName(os));
        Assert.Equal(channel, UpdateChannels.FromFeedName(feed, os));
    }

    [Theory]
    [InlineData("stable", UpdateChannel.Stable)]
    [InlineData("BETA", UpdateChannel.Beta)]
    [InlineData("test", UpdateChannel.Test)]
    [InlineData("", UpdateChannel.Stable)]
    [InlineData("nightly", UpdateChannel.Stable)]
    [InlineData(null, UpdateChannel.Stable)]
    public void Unbekannter_Kanal_ist_stabil(string? key, UpdateChannel expected) => Assert.Equal(expected, UpdateChannels.Parse(key));

    [Fact]
    public void Fremder_Feed_hat_keinen_Kanal() => Assert.Null(UpdateChannels.FromFeedName("osx", "win"));

    [Fact]
    public void Automatische_Pruefung_nur_stabil_ohne_Schalter()
    {
        Assert.Equal([UpdateChannel.Stable], UpdateChannels.AutoChannels(UpdateChannel.Beta, prereleaseAuto: false));
        Assert.Equal([UpdateChannel.Stable], UpdateChannels.AutoChannels(UpdateChannel.Test, prereleaseAuto: false));
        Assert.Equal([UpdateChannel.Stable], UpdateChannels.AutoChannels(UpdateChannel.Stable, prereleaseAuto: true));
        Assert.Equal([UpdateChannel.Stable, UpdateChannel.Beta], UpdateChannels.AutoChannels(UpdateChannel.Beta, prereleaseAuto: true));
    }

    [Fact]
    public void Kanal_Test_schliesst_Beta_ein()
    {
        // TP-WUPDATE: Test ⊇ Beta ⊇ Stabil (Entscheidung 2026-11-06); vorher fehlte Beta, 1.2.0-beta.1 kam nicht an.
        Assert.Equal([UpdateChannel.Stable, UpdateChannel.Beta, UpdateChannel.Test], UpdateChannels.AutoChannels(UpdateChannel.Test, prereleaseAuto: true));
    }

    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.1.0-beta.1", "1.0.1", 1)]
    [InlineData("1.1.0-beta.1", "1.1.0", -1)]
    [InlineData("1.1.0-beta.2", "1.1.0-beta.10", -1)]
    [InlineData("1.1.0-beta.1", "1.1.0-test.3.feat-x", -1)]
    [InlineData("1.1.0-alpha", "1.1.0-alpha.1", -1)]
    // Test-Builds (release.yml): Laufnummer vor dem Branch, damit der spätere Lauf höher ist, egal aus welchem Branch.
    [InlineData("1.1.0-test.50.main", "1.1.0-test.51.feat-a", -1)]
    [InlineData("1.1.0-test.9.zzz", "1.1.0-test.10.aaa", -1)]
    [InlineData("1.0.0+abc", "1.0.0", 0)]
    [InlineData("2.0.0", "10.0.0", -1)]
    public void Versionen_nach_SemVer(string a, string b, int expected)
    {
        Assert.Equal(expected, AppVersion.Compare(a, b));
        Assert.Equal(-expected, AppVersion.Compare(b, a));
    }

    [Fact]
    public void Vorabversion_erkannt()
    {
        Assert.True(AppVersion.IsPrerelease("1.1.0-test.main.4"));
        Assert.False(AppVersion.IsPrerelease("1.1.0+sha"));
    }

    [Theory]
    // Gleicher Kanal wie die laufende Version: kein ExplicitChannel (Velopack nimmt ihn selbst).
    [InlineData("win", "win", false, null, false)]
    [InlineData("win", null, false, null, false)]
    [InlineData("win-beta", "win-beta", false, null, false)]
    // Kanalwechsel: ExplicitChannel; Downgrade nur, wenn ausdrücklich bestätigt.
    [InlineData("win-beta", "win", false, "win-beta", false)]
    [InlineData("win", "win-beta", true, "win", true)]
    [InlineData("win-test", null, false, "win-test", false)]
    // Niedrigere Version im selben Kanal (Versionsliste): nur AllowVersionDowngrade.
    [InlineData("win", "win", true, null, true)]
    public void UpdateOptions_nur_bei_Kanalwechsel_bzw_Downgrade(string channel, string? built, bool downgrade, string? explicitChannel, bool allow)
    {
        var options = VelopackUpdateClient.OptionsFor(channel, built, downgrade, "win");
        Assert.Equal(explicitChannel, options.ExplicitChannel);
        Assert.Equal(allow, options.AllowVersionDowngrade);
    }

    private UpdateService Service(FakeUpdateClient client, UpdatePreferences prefs, List<string> asked, PolicySettings? policy = null) =>
        new(client, new AppLog(_dir), () => policy ?? PolicySettings.None, v =>
        {
            asked.Add(v);
            return Task.FromResult(true);
        }, preferences: () => prefs);

    [Fact]
    public async Task Beta_ohne_Schalter_bietet_nur_stabile_Versionen_an()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient { CurrentVersion = "1.0.0" };
        client.Newer[FakeUpdateClient.Beta] = "1.1.0-beta.1";
        var outcome = await Service(client, new UpdatePreferences(UpdateChannel.Beta, false), asked).CheckOnceAsync(CancellationToken.None);
        Assert.Equal(UpdateOutcome.Current, outcome);
        Assert.Equal([FakeUpdateClient.Stable], client.Checked);
        Assert.Empty(asked);
    }

    [Fact]
    public async Task Beta_mit_Schalter_bietet_die_Beta_an()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient { CurrentVersion = "1.0.0" };
        client.Newer[FakeUpdateClient.Stable] = "1.0.1";
        client.Newer[FakeUpdateClient.Beta] = "1.1.0-beta.1";
        var outcome = await Service(client, new UpdatePreferences(UpdateChannel.Beta, true), asked).CheckOnceAsync(CancellationToken.None);
        Assert.Equal(UpdateOutcome.Applied, outcome);
        Assert.Equal(["1.1.0-beta.1"], asked);
        // Automatisch nie als Downgrade.
        Assert.Equal([("1.1.0-beta.1", FakeUpdateClient.Beta, false)], client.Downloaded);
    }

    [Fact]
    public async Task Spaeterer_stabiler_Release_schlaegt_die_Beta()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient { CurrentVersion = "1.1.0-beta.2" };
        client.Newer[FakeUpdateClient.Stable] = "1.1.0";
        client.Newer[FakeUpdateClient.Beta] = "1.1.0-beta.3";
        await Service(client, new UpdatePreferences(UpdateChannel.Beta, true), asked).CheckOnceAsync(CancellationToken.None);
        Assert.Equal(["1.1.0"], asked);
    }

    [Fact]
    public async Task Richtlinie_sperrt_auch_den_Vorab_Kanal()
    {
        var asked = new List<string>();
        var client = new FakeUpdateClient();
        client.Newer[FakeUpdateClient.Test] = "1.1.0-test.x.1";
        var outcome = await Service(client, new UpdatePreferences(UpdateChannel.Test, true), asked, new PolicySettings { UpdatesEnabled = false })
            .CheckOnceAsync(CancellationToken.None);
        Assert.Equal(UpdateOutcome.DisabledByPolicy, outcome);
        Assert.Empty(client.Checked);
    }

    [Fact]
    public async Task Cli_nutzt_den_eingestellten_Kanal()
    {
        var client = new FakeUpdateClient { CurrentVersion = "1.0.0" };
        client.Newer[FakeUpdateClient.Test] = "1.1.0-test.7.feat-x";
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await UpdateCommand.RunAsync(client, PolicySettings.None, new AppLog(_dir), output, error, CancellationToken.None,
            new UpdatePreferences(UpdateChannel.Test, false));
        Assert.Equal(0, code);
        Assert.Equal([FakeUpdateClient.Stable, FakeUpdateClient.Beta, FakeUpdateClient.Test], client.Checked); // Test ⊇ Beta (TP-WUPDATE)
        Assert.Contains("1.1.0-test.7.feat-x", output.ToString());
    }

    [Fact]
    public void Vertrag_settings_json_Namen_Standard_und_Doku()
    {
        // docs/settings-format.md, Abschnitt „Update-Kanal“: desktop.updateChannel (Text, "stable"), desktop.updatePrereleaseAuto (bool, false).
        var s = new Core.Contracts.Settings.WhisperLoomSettings();
        SettingsStore.SetDesktop(s, new DesktopSettings());
        var json = Core.Contracts.Settings.SettingsJson.Serialize(s);
        Assert.Contains("\"updateChannel\": \"stable\"", json);
        Assert.Contains("\"updatePrereleaseAuto\": false", json);

        var back = SettingsStore.Desktop(SettingsStore.Read("""{"desktop": {"updateChannel": "test", "updatePrereleaseAuto": true}}"""));
        Assert.Equal(new UpdatePreferences(UpdateChannel.Test, true), UpdatePreferences.From(back));
        var unknown = SettingsStore.Desktop(SettingsStore.Read("""{"desktop": {"updateChannel": "nightly"}}"""));
        Assert.Equal(UpdatePreferences.Default, UpdatePreferences.From(unknown));

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WhisperLoom.slnx")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        var doc = File.ReadAllText(Path.Combine(dir.FullName, "docs", "settings-format.md"));
        Assert.Contains("| `updateChannel` | Text | `\"stable\"` |", doc);
        Assert.Contains("| `updatePrereleaseAuto` | bool | `false` |", doc);
    }

    [Fact]
    public void Einstellungen_Standard_ist_stabil_ohne_Vorab()
    {
        Assert.Equal(UpdatePreferences.Default, UpdatePreferences.From(new DesktopSettings()));
        Assert.Equal(new UpdatePreferences(UpdateChannel.Beta, true), UpdatePreferences.From(new DesktopSettings { UpdateChannel = "beta", UpdatePrereleaseAuto = true }));
    }
}
