using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Features.Reinsert;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Tray;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Verlauf-Extras (WLD-P5b): die letzten 10 im Tray, erneut einfügen, neu verarbeiten ohne Audio.</summary>
public sealed class HistoryExtrasTest
{
    private static HistoryEntry Entry(int n, string text) => new(
        $"{1761825600000 + n}-{n:x8}", new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero).AddMinutes(n), "de", 1000,
        "roh " + n, text, new Refinement(RefineMode.Polish), "Fake-STT");

    private static List<HistoryEntry> NewestFirst(int count) =>
        [.. Enumerable.Range(1, count).Reverse().Select(n => Entry(n, $"Diktat {n}"))];

    [Fact]
    public void Tray_takes_the_last_ten_newest_first()
    {
        var recent = RecentHistory.Take(NewestFirst(12));
        Assert.Equal(10, recent.Count);
        Assert.Equal("Diktat 12", recent[0].Text);
        Assert.Equal("Diktat 3", recent[^1].Text);
        Assert.Equal(3, RecentHistory.Take(NewestFirst(3)).Count);
        Assert.Empty(RecentHistory.Take([]));
    }

    [Theory]
    [InlineData("Kurz.", "Kurz.")]
    [InlineData("  Zwei\nZeilen \r\n  hier ", "Zwei Zeilen hier")]
    [InlineData("Ein sehr langes Diktat, das nicht in eine Menüzeile passt, weil es lang ist.", "Ein sehr langes Diktat, das nicht in ein…")]
    [InlineData("   ", "…")]
    public void Label_is_one_short_line(string text, string expected) =>
        Assert.Equal(expected, RecentHistory.Label(Entry(1, text)));

    [AvaloniaFact]
    public void Tray_menu_shows_ten_entries_and_a_click_hands_over_the_entry()
    {
        using var f = new ControllerFixture();
        HistoryEntry? clicked = null;
        var tray = new TrayMenu(f.Controller, _ => { }, e =>
        {
            clicked = e;
            return Task.CompletedTask;
        });
        var recent = tray.Menu.Items.OfType<NativeMenuItem>().Single(i => i.Header!.StartsWith(Loc.Instance["Tray_Recent"], StringComparison.Ordinal));
        Assert.False(recent.Menu!.Items.OfType<NativeMenuItem>().Single().IsEnabled);

        tray.SetRecent(RecentHistory.Take(NewestFirst(12)));

        var items = recent.Menu!.Items.OfType<NativeMenuItem>().ToList();
        Assert.Equal(10, items.Count);
        // Wirkung und Uhrzeit im Eintrag (tray.html Befund 5).
        Assert.Equal(Loc.Instance.Format("Tray_RecentPaste", "Diktat 12", RecentHistory.Time(NewestFirst(12)[0])), items[0].Header);
        items[2].Command!.Execute(null);
        Assert.Equal("Diktat 10", clicked?.Text);
    }

    [AvaloniaFact]
    public async Task Recent_and_latest_text_follow_the_history()
    {
        using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } });
        Assert.Null(await f.Controller.LatestTextAsync());
        var changed = 0;
        f.Controller.HistoryChanged += () => changed++;
        for (var i = 0; i < 12; i++)
        {
            f.Pipeline.RawText = $"Diktat {i}";
            f.Now = f.Now.AddMinutes(1);
            f.Controller.Start();
            await f.Controller.StopAsync();
        }

        var recent = await f.Controller.RecentAsync();
        Assert.Equal(10, recent.Count);
        Assert.Equal("Diktat 11", recent[0].Text);
        Assert.Equal(12, changed);
        Assert.Equal("Diktat 11", await f.Controller.LatestTextAsync());
    }

    [AvaloniaFact]
    public async Task Reprocess_refines_the_raw_text_with_another_stage_without_audio()
    {
        using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } });
        f.Pipeline.RawText = "Hallo Welt";
        f.Controller.Start();
        await f.Controller.StopAsync();
        Assert.Equal(1, f.Pipeline.Runs);
        f.Now = f.Now.AddMinutes(1);

        Assert.Equal("done", await f.Controller.ReprocessAsync("beautify"));

        Assert.Equal(1, f.Pipeline.Runs);
        Assert.False(f.Recorder.Running);
        Assert.Equal("[beautify] Hallo Welt", f.Controller.LastResult);
        Assert.Equal("[beautify] Hallo Welt", f.Sink.Written[^1]);
        var history = await f.History.ListAsync(CancellationToken.None);
        Assert.Equal(2, history.Count);
        Assert.Equal((RefineMode.Beautify, "Hallo Welt", FakeDictationPipeline.ModelLabel), (history[0].Refinement.Mode, history[0].Raw, history[0].Model));
        Assert.Equal("off", f.Controller.Settings.Refine.Stage);
        Assert.DoesNotContain("Hallo Welt", f.LogText);
        Assert.Contains("reprocess", f.LogText);
    }

    [AvaloniaFact]
    public async Task Reprocess_by_id_and_its_refusals()
    {
        using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "off" } });
        Assert.Equal("no-entry", await f.Controller.ReprocessAsync("polish"));
        f.Pipeline.RawText = "Erstes";
        f.Controller.Start();
        await f.Controller.StopAsync();
        f.Now = f.Now.AddMinutes(1);
        f.Pipeline.RawText = "Zweites";
        f.Controller.Start();
        await f.Controller.StopAsync();
        var first = (await f.History.ListAsync(CancellationToken.None))[1];

        Assert.Equal("done", await f.Controller.ReprocessAsync("polish", first.Id));
        Assert.Equal("[polish] Erstes", f.Controller.LastResult);
        Assert.Equal("no-entry", await f.Controller.ReprocessAsync("polish", "gibt-es-nicht"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Controller.ReprocessAsync("prompt"));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Controller.ReprocessAsync("quatsch"));

        f.Pipeline.Failure = FakeFailure.Refine;
        Assert.Equal("skipped", await f.Controller.ReprocessAsync("polish"));
    }
}
