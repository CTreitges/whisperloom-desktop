using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Settings;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Neu verarbeiten als Fassung (W2-OUT, Port der Fälle von Android <c>History.reprocess</c>/<c>HistoryJobs</c>): gespeichert, ohne KI
/// nichts gespeichert, während der Rechnung bearbeitet → Bearbeitung bleibt, Eintrag weg, IT-Sperre.
/// </summary>
public sealed class ReprocessVersionTest : IDisposable
{
    private readonly ControllerFixture _f = new(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });

    public void Dispose() => _f.Dispose();

    private async Task<HistoryEntry> Add()
    {
        var e = new HistoryEntry(FileHistoryStore.NewId(_f.Now), _f.Now, "de", 2000, "roh", "Fertig.", new Refinement(RefineMode.Polish), "x", "m");
        await _f.History.AddAsync(e, CancellationToken.None);
        return e;
    }

    [Fact]
    public async Task Gespeichert_ohne_neuen_Eintrag_und_ohne_Ausgabe()
    {
        var e = await Add();
        var result = await _f.Controller.ReprocessVersionAsync(e.Id, "summarize");
        Assert.Equal("saved", result.Code);
        Assert.Equal("[summarize] roh", HistoryVersions.Find(result.Entry!, "summarize")!.Text);
        Assert.Equal(FakeDictationPipeline.ModelLabel, HistoryVersions.Find(result.Entry!, "summarize")!.Model);
        Assert.Single(await _f.History.ListAsync(CancellationToken.None));
        Assert.Empty(_f.Sink.Written);
        Assert.DoesNotContain("roh", _f.LogText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ohne_KI_wird_nichts_gespeichert()
    {
        var e = await Add();
        _f.Pipeline.Failure = FakeFailure.Refine;
        var result = await _f.Controller.ReprocessVersionAsync(e.Id, "beautify");
        Assert.Equal("skipped", result.Code);
        Assert.Null(HistoryVersions.Find((await _f.History.GetAsync(e.Id, CancellationToken.None))!, "beautify"));
    }

    [Fact]
    public async Task Aus_speichert_die_Regeln_Fassung()
    {
        var e = await Add();
        Assert.Equal("saved", (await _f.Controller.ReprocessVersionAsync(e.Id, "off")).Code);
    }

    [Fact]
    public async Task Waehrend_der_Rechnung_bearbeitet_bleibt_die_Bearbeitung()
    {
        var e = await Add();
        _f.Pipeline.Delay = TimeSpan.FromMilliseconds(200);
        var running = _f.Controller.ReprocessVersionAsync(e.Id, "polish");
        await _f.Controller.EditHistoryAsync(e.Id, "polish", "Von Hand");
        var result = await running;
        Assert.Equal("kept", result.Code);
        Assert.Equal("Von Hand", (await _f.History.GetAsync(e.Id, CancellationToken.None))!.Text);
    }

    [Fact]
    public async Task Geloeschter_Eintrag_bleibt_geloescht()
    {
        var e = await Add();
        _f.Pipeline.Delay = TimeSpan.FromMilliseconds(200);
        var running = _f.Controller.ReprocessVersionAsync(e.Id, "summarize");
        await _f.History.DeleteAsync(e.Id, CancellationToken.None);
        Assert.Equal("gone", (await running).Code);
        Assert.Empty(await _f.History.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task IT_Sperre_und_unbekannte_Stufe()
    {
        var e = await Add();
        _f.Controller.Policy = new PolicySettings { Locked = ["refine.stage"] };
        Assert.Equal("stage-locked", (await _f.Controller.ReprocessVersionAsync(e.Id, "summarize")).Code);
        await Assert.ThrowsAsync<ArgumentException>(() => _f.Controller.ReprocessVersionAsync(e.Id, "quatsch"));
    }
}
