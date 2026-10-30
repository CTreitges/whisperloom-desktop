using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.Core.Tests.History;

/// <summary>
/// Port der übertragbaren Fälle von <c>history/HistoryTest.kt</c> auf den Desktop-Vertrag
/// <see cref="IHistoryStore"/>: Rundreise, Größe und Kürzen, Reste und kaputte Dateien, gleichzeitige
/// Schreiber, Löschen und Wiederherstellen — am echten Dateisystem in einem Temp-Ordner.
/// </summary>
public sealed class FileHistoryStoreTest : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"wl-history-test-{Guid.NewGuid():N}");
    private readonly HistorySettings settings = new();
    private readonly FileHistoryStore store;
    private long now = 1_791_456_000_000L;

    public FileHistoryStoreTest()
    {
        store = new FileHistoryStore(Dir, () => settings);
    }

    private string Dir => Path.Combine(root, "history");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (System.IO.Directory.Exists(root))
        {
            System.IO.Directory.Delete(root, recursive: true);
        }
    }

    private HistoryEntry Entry(
        string raw = "ähm also ich komme morgen später",
        string text = "Komme morgen später.",
        Refinement? refinement = null,
        string? model = "Claude Sonnet 5.5",
        string? skipped = null)
    {
        var createdAt = DateTimeOffset.FromUnixTimeMilliseconds(now++);
        return new HistoryEntry(
            FileHistoryStore.NewId(createdAt), createdAt, "de", 41_000L, raw, text,
            refinement ?? new Refinement(RefineMode.Summarize), "OpenAI · whisper-1", model, skipped);
    }

    private Task Record(HistoryEntry? entry = null) => store.AddAsync(entry ?? Entry(), Ct);

    private async Task<int> Count() => (await store.ListAsync(Ct)).Count;

    // --- Rundreise --------------------------------------------------------------------------

    [Fact]
    public async Task RundreiseMitUmlautenEmojiUndZwanzigtausendZeichen()
    {
        var lang = "Grüße 😀 „Zitat“ \"roh\" \\ ß\nZeile zwei. " + new string('x', 20_000);
        var recorded = Entry(raw: lang, text: "Fertig: " + lang, skipped: "API-Fehler 401");
        await Record(recorded);

        var entry = Assert.Single(await store.ListAsync(Ct));

        Assert.Equal(recorded, entry);
        Assert.Equal(entry, await store.GetAsync(entry.Id, Ct));
        Assert.Equal(lang, entry.Raw);
        Assert.Equal("de", entry.Language);
        Assert.Equal(41_000L, entry.DurationMs);
        Assert.Equal(1_791_456_000_000L, entry.CreatedAt.ToUnixTimeMilliseconds());
        Assert.StartsWith("1791456000000-", entry.Id);
        Assert.Equal(new Refinement(RefineMode.Summarize), entry.Refinement);
        Assert.Equal("Claude Sonnet 5.5", entry.Model);
        Assert.Equal("API-Fehler 401", entry.Skipped);
    }

    [Fact]
    public async Task GlaettenSpeichertDieBereinigungMit()
    {
        await Record(Entry(refinement: new Refinement(RefineMode.Polish, SmartFillers: true)));
        await Record(Entry(refinement: new Refinement(RefineMode.Readable, Paragraphs: false)));
        await Record(Entry(refinement: Refinement.Off, model: null));

        var list = await store.ListAsync(Ct);

        Assert.Equal(
            [Refinement.Off, new Refinement(RefineMode.Readable, Paragraphs: false), new Refinement(RefineMode.Polish, SmartFillers: true)],
            list.Select(e => e.Refinement));
        Assert.Null(list[0].Model); // neueste zuerst
    }

    [Fact]
    public async Task LeereErkennungWirdNichtGespeichert()
    {
        await Record(Entry(raw: " \n", text: ""));
        Assert.Equal(0, await Count());
        Assert.False(System.IO.Directory.Exists(Dir));
    }

    [Fact]
    public async Task LiegtAlsJsonJeEintragImDatenordner()
    {
        await Record();
        var id = Assert.Single(await store.ListAsync(Ct)).Id;

        Assert.True(File.Exists(Path.Combine(Dir, id + ".json")));
        Assert.Equal([id + ".json"], System.IO.Directory.GetFiles(Dir).Select(Path.GetFileName));
    }

    [Fact]
    public async Task ToStringEnthaeltKeinenText()
    {
        await Record(Entry(raw: "geheimer Rohtext", text: "Geheime Fassung."));
        var s = Assert.Single(await store.ListAsync(Ct)).ToString();
        Assert.DoesNotContain("geheim", s, StringComparison.OrdinalIgnoreCase);
    }

    // --- Größe, aus, kürzen ----------------------------------------------------------------

    [Fact]
    public async Task AusgeschaltetSchreibtNichts()
    {
        settings.Enabled = false;
        await Record();
        Assert.False(System.IO.Directory.Exists(Dir));
    }

    [Fact]
    public async Task AusschaltenUndLeerenLoeschtAllesEinschaltenSchreibtWieder()
    {
        // Android löscht beim Ausschalten selbst (setEnabled); hier ruft die Einstellung ClearAsync.
        await Record();
        await Record();
        settings.Enabled = false;
        await store.ClearAsync(Ct);
        await Record();
        Assert.Equal(0, await Count());

        settings.Enabled = true;
        await Record();
        Assert.Equal(1, await Count());
    }

    [Fact]
    public async Task EinfuegenKuerztAufDieNeuestenN()
    {
        settings.Size = 10;
        for (var i = 0; i < 12; i++)
        {
            await Record(Entry(raw: $"diktat {i}"));
        }

        var list = await store.ListAsync(Ct);

        Assert.Equal(10, list.Count);
        Assert.Equal(Enumerable.Range(2, 10).Reverse().Select(i => $"diktat {i}"), list.Select(e => e.Raw));
        Assert.Equal(10, System.IO.Directory.GetFiles(Dir).Length); // keine Datei der zwei ältesten mehr
    }

    /// <summary>Uhr zurückgestellt: die neue Id ist die älteste, das Kürzen löscht sie gleich wieder.</summary>
    [Fact]
    public async Task EinEintragDenDasKuerzenGleichWiederLoeschtIstNichtImVerlauf()
    {
        settings.Size = 10;
        for (var i = 0; i < 10; i++)
        {
            await Record(Entry(raw: $"diktat {i}"));
        }
        now -= 3_600_000;

        await Record(Entry(raw: "mit alter Uhr"));

        var list = await store.ListAsync(Ct);
        Assert.Equal(10, list.Count);
        Assert.DoesNotContain(list, e => e.Raw == "mit alter Uhr");
    }

    [Fact]
    public async Task VerkleinernKuerztBeimNaechstenEinfuegenVergroessernLoeschtNichts()
    {
        settings.Size = 50;
        for (var i = 0; i < 30; i++)
        {
            await Record(Entry(raw: $"diktat {i}"));
        }

        settings.Size = 10;
        await Record(Entry(raw: "diktat 30"));
        Assert.Equal(10, await Count());
        Assert.Equal("diktat 30", (await store.ListAsync(Ct))[0].Raw);

        settings.Size = 100;
        await Record(Entry(raw: "diktat 31"));
        Assert.Equal(11, await Count());
    }

    [Fact]
    public async Task NichtWaehlbareGroesseGiltAlsStandard()
    {
        // Android lehnt sie ab (setSize); im Einstellungs-Modell gilt Unbekanntes als 50.
        settings.Size = 7;
        for (var i = 0; i < 52; i++)
        {
            await Record(Entry(raw: $"diktat {i}"));
        }
        Assert.Equal(HistorySettings.DefaultSize, await Count());
    }

    // --- Reste, kaputte Dateien, IO-Fehler ------------------------------------------------------

    [Fact]
    public async Task ResteAbgebrochenerSchreibvorgaengeVerschwindenKaputteWerdenUebersprungen()
    {
        await Record(Entry(raw: "gut eins"));
        await Record(Entry(raw: "gut zwei"));
        var waise = Path.Combine(Dir, "1791456000500-0000abcd.json.tmp");
        File.WriteAllText(waise, """{"raw":"halber Text""");
        File.WriteAllText(Path.Combine(Dir, "1791456000502-0000abcd.json"), "kein JSON");
        File.WriteAllText(Path.Combine(Dir, "1791456000503-0000abcd.json"), """{"v":1,"raw":""}""");
        File.WriteAllText(Path.Combine(Dir, "1791456000504-0000abcd.json"), """[1,2]""");

        var list = await store.ListAsync(Ct);

        Assert.Equal(["gut zwei", "gut eins"], list.Select(e => e.Raw));
        Assert.False(File.Exists(waise), "verwaiste .tmp enthält Text");
    }

    [Fact]
    public async Task FehlendeUndUnbekannteFelderSindTolerant()
    {
        System.IO.Directory.CreateDirectory(Dir);
        File.WriteAllText(
            Path.Combine(Dir, "1791456000123-3fa9c1d2.json"),
            """{"v":9,"raw":"also hallo","neu":true,"refinement":{"mode":"zaubern"},"model":null}""");

        var entry = Assert.Single(await store.ListAsync(Ct));

        Assert.Equal(1_791_456_000_123L, entry.CreatedAt.ToUnixTimeMilliseconds());
        Assert.Equal(0L, entry.DurationMs);
        Assert.Equal("auto", entry.Language);
        Assert.Equal(Refinement.Off, entry.Refinement); // unbekannte Stufe gilt als aus
        Assert.Equal("", entry.Text);
        Assert.Equal("", entry.SttLabel);
        Assert.Null(entry.Model);
    }

    [Fact]
    public async Task IoFehlerWirftIOException()
    {
        // Android schluckt den Fehler (record = false); der Desktop-Vertrag wirft IOException.
        System.IO.Directory.CreateDirectory(root);
        File.WriteAllText(Dir, "ich bin eine Datei, kein Ordner");
        await Assert.ThrowsAnyAsync<IOException>(() => Record());
    }

    [Fact]
    public async Task FremdeIdsWerdenNichtGelesen()
    {
        await Record();
        Assert.Null(await store.GetAsync("../../settings", Ct));
        Assert.False(await store.DeleteAsync("1791456000000-zzzzzzzz", Ct));
        await Assert.ThrowsAsync<ArgumentException>(() => Record(Entry() with { Id = "../x" }));
    }

    // --- Gleichzeitig -----------------------------------------------------------------------

    [Fact]
    public async Task GleichzeitigeSchreiberVerlierenNichts()
    {
        settings.Size = 500;
        var stamp = DateTimeOffset.FromUnixTimeMilliseconds(now);
        // Viele Diktate in derselben Millisekunde: die Ids unterscheiden sich nur im Zufallsteil.
        var writers = Enumerable.Range(0, 8).Select(t => Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                await store.AddAsync(
                    new HistoryEntry(FileHistoryStore.NewId(stamp), stamp, "de", 1, $"faden {t} diktat {i}", "x", Refinement.Off, "stt"),
                    Ct);
            }
        }));
        await Task.WhenAll(writers);

        var list = await store.ListAsync(Ct);
        Assert.Equal(80, list.Count);
        Assert.Equal(80, list.Select(e => e.Raw).Distinct().Count());
    }

    // --- Löschen und Rückgängig --------------------------------------------------------------

    [Fact]
    public async Task LoeschenUndRueckgaengig()
    {
        await Record(Entry(raw: "eins"));
        await Record(Entry(raw: "zwei"));
        var zwei = (await store.ListAsync(Ct))[0];

        Assert.True(await store.DeleteAsync(zwei.Id, Ct));
        Assert.False(await store.DeleteAsync(zwei.Id, Ct));
        Assert.Null(await store.GetAsync(zwei.Id, Ct));
        Assert.Equal(["eins"], (await store.ListAsync(Ct)).Select(e => e.Raw));

        // Rückgängig = derselbe Eintrag noch einmal, unter seiner alten Id.
        await Record(zwei);
        Assert.Equal(["zwei", "eins"], (await store.ListAsync(Ct)).Select(e => e.Raw));
        Assert.Equal(zwei, await store.GetAsync(zwei.Id, Ct));
    }

    [Fact]
    public async Task AlleLoeschenLeertDenOrdner()
    {
        for (var i = 0; i < 3; i++)
        {
            await Record();
        }
        await store.ClearAsync(Ct);
        Assert.Equal(0, await Count());
        Assert.False(System.IO.Directory.Exists(Dir));
        await store.ClearAsync(Ct); // ohne Ordner kein Fehler
    }

    // --- Desktop: Ids --------------------------------------------------------------------------

    [Fact]
    public void IdsSortierenWieDieZeit()
    {
        var early = FileHistoryStore.NewId(DateTimeOffset.FromUnixTimeMilliseconds(999));
        var late = FileHistoryStore.NewId(DateTimeOffset.FromUnixTimeMilliseconds(1_791_456_000_000));
        Assert.Matches("^0000000000999-[0-9a-f]{8}$", early);
        Assert.True(FileHistoryStore.IsId(late));
        Assert.True(string.CompareOrdinal(early, late) < 0);
        Assert.False(FileHistoryStore.IsId("1791456000000-ABCDEF12"));
        Assert.False(FileHistoryStore.IsId("1791456000000-abcdef12.json"));
    }
}
