using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.Core.Tests.History;

/// <summary>
/// Fassungen je Verarbeitung (W2-OUT), Port der Fälle aus Android <c>HistoryTest.kt</c> (setVersion, edit, reprocess „kept“):
/// Ursprung nie überschrieben, Ersetzen am Platz, „bearbeitet“, alte Dateien ohne Fassungen laden unverändert.
/// </summary>
public sealed class HistoryVersionsTest : IDisposable
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.FromUnixTimeMilliseconds(1_791_456_000_000L);
    private readonly string root = Path.Combine(Path.GetTempPath(), $"wl-versions-test-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HistoryEntry Entry() =>
        new(FileHistoryStore.NewId(T0), T0, "de", 4000, "ähm also morgen später", "Morgen später.", new Refinement(RefineMode.Polish), "OpenAI", "gpt");

    [Fact]
    public void OhneListeGibtEsGenauDieDamaligeFassung()
    {
        var all = HistoryVersions.All(Entry());
        var only = Assert.Single(all);
        Assert.Equal(new HistoryVersion("polish", "Morgen später.", T0, "gpt"), only);
    }

    [Fact]
    public void NeueFassungKommtHintenDazuUndUrsprungBleibt()
    {
        var e = HistoryVersions.With(Entry(), new HistoryVersion("summarize", "Kurz.", T0.AddMinutes(1), "m2"));
        Assert.Equal(["polish", "summarize"], HistoryVersions.All(e).Select(v => v.Processing));
        Assert.Equal("ähm also morgen später", e.Raw);
        Assert.Equal("Morgen später.", e.Text);
    }

    [Fact]
    public void ErsetzenBehaeltDenPlatz()
    {
        var e = HistoryVersions.With(Entry(), new HistoryVersion("summarize", "Kurz.", T0));
        e = HistoryVersions.With(e, new HistoryVersion("polish", "Neu geglättet.", T0.AddMinutes(2)));
        Assert.Equal(["polish", "summarize"], HistoryVersions.All(e).Select(v => v.Processing));
        Assert.Equal("Neu geglättet.", e.Text);
    }

    [Fact]
    public void BearbeitenMarkiertUndBehaeltModell()
    {
        var e = HistoryVersions.Edit(Entry(), "polish", "Von Hand.", T0.AddHours(1));
        var v = HistoryVersions.Find(e, "polish")!;
        Assert.True(v.Edited);
        Assert.Equal("gpt", v.Model);
        Assert.Equal("Von Hand.", e.Text);
        Assert.True(HistoryVersions.NeedsConfirmBeforeReplace(e, "polish"));
        Assert.False(HistoryVersions.NeedsConfirmBeforeReplace(e, "summarize"));
    }

    [Fact]
    public async Task UrsprungWirdNieUeberschrieben()
    {
        var store = new FileHistoryStore(Path.Combine(root, "history"), () => new HistorySettings());
        var e = Entry();
        await store.AddAsync(e, Ct);
        var edited = HistoryVersions.Edit(e, HistoryVersions.Edited, "geänderter Ursprung", T0.AddHours(1));
        await store.AddAsync(edited, Ct);
        var back = (await store.GetAsync(e.Id, Ct))!;
        Assert.Equal("ähm also morgen später", back.Raw);
        Assert.Equal("geänderter Ursprung", HistoryVersions.Find(back, HistoryVersions.Edited)!.Text);
        Assert.Equal("Morgen später.", back.Text);
        Assert.Single(await store.ListAsync(Ct));
    }

    [Fact]
    public async Task RundreiseMitFassungen()
    {
        var store = new FileHistoryStore(Path.Combine(root, "history"), () => new HistorySettings());
        var e = HistoryVersions.With(Entry(), new HistoryVersion("prompt", "Prompt 😀", T0.AddSeconds(5), null, "offline", Edited: true));
        await store.AddAsync(e, Ct);
        var back = (await store.GetAsync(e.Id, Ct))!;
        Assert.Equal(HistoryVersions.All(e), HistoryVersions.All(back));
    }

    [Fact]
    public async Task AlteDateiOhneFassungenLaedtUnveraendert()
    {
        var dir = Path.Combine(root, "history");
        Directory.CreateDirectory(dir);
        const string id = "1791456000000-0000abcd";
        // Format von Lauf 1 (vor W2-OUT): kein Feld "versions".
        await File.WriteAllTextAsync(Path.Combine(dir, id + ".json"),
            """{"v":1,"createdAt":1791456000000,"language":"de","durationMs":4000,"raw":"roh","text":"Fertig.","refinement":{"mode":"polish","smartFillers":false,"paragraphs":true},"sttLabel":"OpenAI","model":"gpt","skipped":null}""",
            Ct);
        var store = new FileHistoryStore(dir, () => new HistorySettings());
        var e = (await store.GetAsync(id, Ct))!;
        Assert.Null(e.Versions);
        Assert.Equal(("roh", "Fertig.", "gpt"), (e.Raw, e.Text, e.Model));
        // Unverändert zurückgeschrieben: weiterhin ohne "versions".
        await store.AddAsync(e, Ct);
        Assert.DoesNotContain("versions", await File.ReadAllTextAsync(Path.Combine(dir, id + ".json"), Ct), StringComparison.Ordinal);
    }

    [Fact]
    public void ToStringOhneText()
    {
        var e = HistoryVersions.With(Entry(), new HistoryVersion("prompt", "GEHEIM", T0));
        Assert.DoesNotContain("GEHEIM", e.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("GEHEIM", HistoryVersions.Find(e, "prompt")!.ToString(), StringComparison.Ordinal);
    }
}
