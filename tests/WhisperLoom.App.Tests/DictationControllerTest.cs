using WhisperLoom.App.Services;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests;

public class DictationControllerTest
{
    private static WhisperLoomSettings PromptSettings() =>
        new() { Language = "en", Refine = new RefineSettings { Stage = "prompt", PromptEnabled = true } };

    [Fact]
    public async Task Toggle_twice_outputs_result_adds_history_and_logs_only_metadata()
    {
        using var f = new ControllerFixture(PromptSettings());

        Assert.Equal("recording", await f.Controller.ToggleAsync());
        Assert.Equal(DictationState.Recording, f.Controller.State);
        Assert.Equal("done", await f.Controller.ToggleAsync());

        Assert.Equal(DictationState.Idle, f.Controller.State);
        Assert.Equal(["[prompt] Hallo Welt geheimes Diktat"], f.Sink.Written);
        Assert.Equal("Hallo Welt geheimes Diktat", f.Controller.LastRaw);
        var history = await f.History.ListAsync(CancellationToken.None);
        var entry = Assert.Single(history);
        Assert.Equal("[prompt] Hallo Welt geheimes Diktat", entry.Text);
        Assert.Equal("en", entry.Language);
        Assert.Contains("dictation status=ok durationMs=1000", f.LogText);
        Assert.DoesNotContain("geheimes", f.LogText);
    }

    [Fact]
    public async Task Pipeline_error_sets_error_key_and_writes_nothing()
    {
        using var f = new ControllerFixture();
        f.Pipeline.Failure = FakeFailure.SttHttp;

        f.Controller.Start();
        Assert.Equal("error", await f.Controller.StopAsync());

        Assert.Equal("Error_Http", f.Controller.LastErrorKey);
        Assert.Empty(f.Sink.Written);
        Assert.Contains("errorClass=ApiHttpException httpStatus=500", f.LogText);
    }

    [Fact]
    public void Missing_microphone_reports_audio_error()
    {
        using var f = new ControllerFixture();
        f.Recorder.Unavailable = true;

        Assert.Equal("audio-unavailable", f.Controller.Start());
        Assert.Equal(DictationState.Idle, f.Controller.State);
        Assert.Equal("Error_Audio", f.Controller.LastErrorKey);
    }

    [Fact]
    public void Pause_is_not_counted_in_elapsed_time()
    {
        using var f = new ControllerFixture();
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(3);
        f.Controller.Pause();
        f.Now += TimeSpan.FromSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(3), f.Controller.Elapsed);
        f.Controller.Resume();
        f.Now += TimeSpan.FromSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(5), f.Controller.Elapsed);
    }

    [Fact]
    public void Cancel_discards_recording()
    {
        using var f = new ControllerFixture();
        f.Controller.Start();
        Assert.Equal("cancelled", f.Controller.Cancel());
        Assert.Equal(DictationState.Idle, f.Controller.State);
        Assert.Equal(0, f.Pipeline.Runs);
    }

    [Fact]
    public void Stage_next_cycles_and_saves()
    {
        using var f = new ControllerFixture();
        Assert.Equal(RefineMode.Polish, f.Controller.SetStage("next"));
        Assert.Equal(RefineMode.Beautify, f.Controller.SetStage("next"));
        Assert.Equal(RefineMode.Summarize, f.Controller.SetStage("next"));
        Assert.Equal(RefineMode.Off, f.Controller.SetStage("next"));
        Assert.Equal("off", f.Store.Load().Refine.Stage);
    }

    [Fact]
    public void Prompt_stage_only_when_enabled()
    {
        using var f = new ControllerFixture();
        Assert.Throws<ArgumentException>(() => f.Controller.SetStage("prompt"));
        Assert.Throws<ArgumentException>(() => f.Controller.SetStage("readable"));
        f.Controller.Settings.Refine.PromptEnabled = true;
        Assert.Equal(RefineMode.Prompt, f.Controller.SetStage("prompt"));
    }

    [Theory]
    [InlineData("paragraphs", false, RefineMode.Polish)]
    [InlineData("prompt", false, RefineMode.Polish)]
    [InlineData("prompt", true, RefineMode.Prompt)]
    [InlineData("readable", false, RefineMode.Polish)]
    [InlineData("unbekannt", false, RefineMode.Off)]
    public void StageOf_follows_android_rules(string stored, bool promptEnabled, RefineMode expected)
    {
        Assert.Equal(expected, DictationController.StageOf(new RefineSettings { Stage = stored, PromptEnabled = promptEnabled }));
    }

    [Fact]
    public async Task Switching_history_off_clears_existing_entries()
    {
        using var f = new ControllerFixture();
        f.Controller.Start();
        await f.Controller.StopAsync();
        Assert.Single(await f.History.ListAsync(CancellationToken.None));

        // Wie das Einstellungsfenster: eine geänderte Kopie übernehmen.
        var changed = SettingsJson.Deserialize(SettingsJson.Serialize(f.Controller.Settings));
        changed.History.Enabled = false;
        await f.Controller.ApplySettingsAsync(changed);

        Assert.Empty(await f.History.ListAsync(CancellationToken.None));
        Assert.Contains("history.cleared reason=disabled", f.LogText);
    }

    [Fact]
    public async Task Other_setting_changes_keep_history()
    {
        using var f = new ControllerFixture();
        f.Controller.Start();
        await f.Controller.StopAsync();
        var changed = SettingsJson.Deserialize(SettingsJson.Serialize(f.Controller.Settings));
        changed.Language = "en";
        await f.Controller.ApplySettingsAsync(changed);
        Assert.Single(await f.History.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task History_off_stores_nothing()
    {
        var settings = new WhisperLoomSettings { History = new HistorySettings { Enabled = false } };
        using var f = new ControllerFixture(settings);
        f.Controller.Start();
        await f.Controller.StopAsync();
        Assert.Empty(await f.History.ListAsync(CancellationToken.None));
    }
}
