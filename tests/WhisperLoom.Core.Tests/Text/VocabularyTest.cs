using System.Text;
using System.Text.RegularExpressions;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Tests.Text;

/// <summary>Port von <c>VocabularyTest.kt</c>: Liste, Datei-Parser und der gekappte Prompt für den Erkenner.</summary>
public class VocabularyTest
{
    [Fact]
    public void FreitextAusAlterVersionBleibtEinEintragUndKommtUnveraendertAn()
    {
        const string legacy = "Christof Treitges, WhisperLoom, Ollama";
        Assert.Equal([legacy], Vocabulary.Entries(legacy));
        Assert.Equal(legacy, Vocabulary.Prompt(Vocabulary.Entries(legacy)).Text);
    }

    [Fact]
    public void EintraegeSindZeilenOhneLeeres()
    {
        Assert.Equal(["Anna", "Bernd"], Vocabulary.Entries("  Anna \n\n Bernd\n"));
        Assert.Empty(Vocabulary.Entries(""));
        Assert.Equal("Anna\nBernd", Vocabulary.Serialize(["Anna", "Bernd"]));
    }

    [Fact]
    public void HinzufuegenTrenntAnKommaUndSemikolonOhneDuplikate()
    {
        var list = Vocabulary.Add(["Anna"], "anna, Bernd; Carla ,, ");
        Assert.Equal(["Anna", "Bernd", "Carla"], list);
    }

    [Fact]
    public void EntfernenNimmtNurDenEintrag()
    {
        Assert.Equal(["Anna", "Carla"], Vocabulary.Remove(["Anna", "Bernd", "Carla"], "Bernd"));
    }

    [Fact]
    public void DateiParserEntferntMarkdown()
    {
        const string md = """
            # Vokabular Firma

            - Treitges
            - **WhisperLoom**
            * Oracle-VPS
            1. Caddy
            2) `systemd`
            - [ ] Tailscale
            - [x] Hermes
            > Zitat-Begriff

            Glm, Gemma; Ollama
            ---
            ```
            code wird ignoriert
            ```
            ## Zweite Ueberschrift
            """;
        Assert.Equal(
            [
                "Treitges", "WhisperLoom", "Oracle-VPS", "Caddy", "systemd", "Tailscale", "Hermes",
                "Zitat-Begriff", "Glm", "Gemma", "Ollama",
            ],
            Vocabulary.ParseFile(md));
    }

    [Fact]
    public void DateiParserMitBomUndWindowsZeilenenden()
    {
        Assert.Equal(["Anna", "Bernd"], Vocabulary.ParseFile("﻿Anna\r\nBernd\r\n"));
    }

    [Fact]
    public void DateiParserEntferntDuplikateUnabhaengigVonGrossschreibung()
    {
        Assert.Equal(["Ollama"], Vocabulary.ParseFile("Ollama\n- ollama\nOLLAMA"));
    }

    [Fact]
    public void EigeneBegriffeStehenAmEndeUndGewinnenBeiDuplikaten()
    {
        // Whisper beachtet nur das ENDE des Prompts — die eigenen Begriffe gehören dorthin.
        var p = Vocabulary.Prompt(["Anna", "Bernd"], ["bernd", "Carla"]);
        Assert.Equal("Carla, Anna, Bernd", p.Text);
        Assert.Equal(3, p.Used);
        Assert.Equal(3, p.Total);
        Assert.False(p.Truncated);
    }

    [Fact]
    public void KappungWirftZuerstVordereDateiBegriffe()
    {
        var file = Enumerable.Range(1, 400).Select(i => $"Begriff{i}").ToList();
        var p = Vocabulary.Prompt(["Anna"], file);
        Assert.True(p.Text.Length <= Vocabulary.MaxPromptChars);
        Assert.True(p.Truncated);
        Assert.Equal(401, p.Total);
        // Eigener Begriff bleibt, und zwar am Ende; davor die letzten Datei-Begriffe.
        Assert.EndsWith("Begriff399, Begriff400, Anna", p.Text);
        Assert.DoesNotContain("Begriff1,", p.Text);
        // Erster Begriff ist vollständig — nie mitten im Wort abgeschnitten.
        Assert.Matches(new Regex(@"^Begriff\d+$"), p.Text.Split(", ")[0]);
        Assert.Equal(p.Used, p.Text.Split(", ").Length);
    }

    [Fact]
    public void LangerEinzelnerFreitextWirdAmWortendeGekuerztUndAlsGekuerztGemeldet()
    {
        var legacy = string.Join(" ", Enumerable.Repeat("wort", 300));
        var p = Vocabulary.Prompt([legacy]);
        Assert.True(p.Text.Length <= Vocabulary.MaxPromptChars);
        // Am Wortende geschnitten, der Schnitt mit "…" markiert.
        Assert.EndsWith(" wort…", p.Text);
        Assert.Equal(1, p.Used);
        Assert.Equal(1, p.Total);
        Assert.True(p.Truncated);
    }

    [Fact]
    public void MehrzeiligerFreitextAusAlterVersionWirdZuEintraegen()
    {
        // Das alte Feld war mehrzeilig: jede Zeile wird ein Eintrag, verbunden mit ", ".
        const string legacy = "Projekt X.\nTeilnehmer: Anna, Bernd";
        Assert.Equal(["Projekt X.", "Teilnehmer: Anna, Bernd"], Vocabulary.Entries(legacy));
        Assert.Equal("Projekt X., Teilnehmer: Anna, Bernd", Vocabulary.Prompt(Vocabulary.Entries(legacy)).Text);
    }

    [Fact]
    public void DateiParserVerwirftZeichenOhneInhaltTabellenUndLeereAufgaben()
    {
        const string md = "-\n+\n- [ ]\n***\n| Name | Rolle |\n|---|---|\n| Anna | Chefin |\n- Echt";
        Assert.Equal(["Echt"], Vocabulary.ParseFile(md));
    }

    [Fact]
    public void Windows1252DateiBehaeltIhreUmlaute()
    {
        var cp1252 = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes("Müller\nGörlitz");
        Assert.Equal("Müller\nGörlitz", VocabularySource.Decode(cp1252));
        var utf8 = Encoding.UTF8.GetBytes("Müller");
        Assert.Equal("Müller", VocabularySource.Decode(utf8));
    }

    [Fact]
    public void TextdateiNachEndungOderTyp()
    {
        Assert.True(VocabularySource.IsTextFile("namen.md"));
        Assert.True(VocabularySource.IsTextFile("Namen.TXT"));
        Assert.True(VocabularySource.IsTextFile("Namen", "text/plain"));
        Assert.True(VocabularySource.IsTextFile(null));
        Assert.False(VocabularySource.IsTextFile("foto.jpg", "image/jpeg"));
        Assert.False(VocabularySource.IsTextFile("archiv.zip", "application/octet-stream"));
    }

    [Fact]
    public void LeerIstLeer()
    {
        var p = Vocabulary.Prompt([]);
        Assert.Equal("", p.Text);
        Assert.Equal(0, p.Total);
    }

    // --- Desktop: Datei lesen (Android liest über den ContentResolver) -----------------------

    [Fact]
    public void DateiBegriffeAusEinerDatei()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wl-vocab-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllBytes(path, CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetBytes("# Namen\n- Müller\nAnna, Bernd"));
            Assert.Equal(["Müller", "Anna", "Bernd"], VocabularySource.FileTerms(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OhneDateiLeerUnlesbarNull()
    {
        Assert.Empty(VocabularySource.FileTerms("  ")!);
        Assert.Null(VocabularySource.FileTerms(Path.Combine(Path.GetTempPath(), $"wl-fehlt-{Guid.NewGuid():N}.md")));
    }
}
