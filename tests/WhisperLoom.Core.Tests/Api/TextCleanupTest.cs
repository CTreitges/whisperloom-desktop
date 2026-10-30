using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Pipeline;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.Core.Tests.Api;

/// <summary>
/// Port von <c>api/TextCleanupTest.kt</c> (<c>TextRefiner.cleanText</c>/<c>finish</c>). Die Füllsilben kommen auf
/// Android aus <c>TextPolisher.builtinFillers</c> (WLD-P1a); hier mit einer festen Liste, ein Fall mit der echten.
/// </summary>
public class TextCleanupTest
{
    private const string Diktat = "also ich wollte nur sagen dass ich morgen nicht kann";

    private static readonly TextCleanup Cleanup = new(["äh", "ähm", "uh", "um"]);

    private static string Clean(string output, string raw = Diktat) => Cleanup.CleanText(raw, output);

    [Fact]
    public void LeereAntwortIstGescheitert()
    {
        string?[] leer = [null, "", "   ", "<think>nachdenken</think>", "<diktat>\n</diktat>", "<dictation></dictation>"];
        foreach (var mode in new[] { RefineMode.Polish, RefineMode.Summarize, RefineMode.Prompt })
        {
            foreach (var output in leer)
            {
                var e = Assert.Throws<RefineRejectedException>(() => Cleanup.Finish(Diktat, mode, output));
                Assert.Equal(TextRefiner.MsgEmpty, e.Message);
            }
        }
        Assert.Equal("Hallo.", Cleanup.Finish(Diktat, RefineMode.Polish, "<think>hm</think>Hallo."));
    }

    [Fact]
    public void SauberGeglaettetBleibtUnveraendert()
    {
        const string text = "Also, ich wollte nur sagen, dass ich morgen nicht kann.\n\nZweiter Absatz.";
        Assert.Equal(text, Clean(text));
    }

    [Fact]
    public void MarkierungenFallenWeg()
    {
        Assert.Equal("Ich kann morgen nicht.", Clean("<diktat>\nIch kann morgen nicht.\n</diktat>"));
        Assert.Equal("I can't make it tomorrow.", Clean("<dictation>I can't make it tomorrow.</dictation>"));
        Assert.Equal("Ich kann morgen nicht.", Clean("Ich kann morgen nicht.</DIKTAT>"));
    }

    [Fact]
    public void VorredeMitZeilenumbruchFaelltWeg()
    {
        Assert.Equal("Ich kann morgen nicht.", Clean("Hier ist der geglättete Text:\nIch kann morgen nicht."));
        Assert.Equal("Ich kann morgen nicht.", Clean("Klar! Hier ist die überarbeitete Fassung:\n\nIch kann morgen nicht."));
        Assert.Equal("- Morgen geht nicht", Clean("Hier ist die Zusammenfassung:\n- Morgen geht nicht"));
        Assert.Equal("I can't make it.", Clean("Here is the cleaned-up text:\nI can't make it.", raw: "i cant make it"));
    }

    [Fact]
    public void DiktierteEinleitungOhneUmbruchBleibt()
    {
        const string text = "Hier ist der Text: Ich kann morgen nicht.";
        Assert.Equal(text, Clean(text, raw: "hier ist der text ich kann morgen nicht"));
        const string liste = "Hier sind meine Punkte:\n- Brot\n- Milch";
        Assert.Equal(liste, Clean(liste, raw: "hier sind meine punkte brot milch"));
    }

    [Fact]
    public void DiktierteEinleitungMitUmbruchDesModellsBleibt()
    {
        const string glaetten = "Okay, hier ist der neue Text:\nLiebe Frau Müller,\nich komme am Montag.";
        Assert.Equal(glaetten, Clean(glaetten, raw: "okay hier ist der neue Text: liebe Frau Müller ich komme am Montag"));
        const string kuerzen = "Hier ist die Zusammenfassung:\n- Angebot schicken\n- Termin am Montag";
        Assert.Equal(kuerzen, Clean(kuerzen, raw: "hier ist die Zusammenfassung vom Meeting wir schicken das Angebot und der Termin ist am Montag"));
        const string en = "Sure, here's the summary:\n- fix login\n- ship Friday";
        Assert.Equal(en, Clean(en, raw: "sure here's the summary we fix login and ship friday"));
        Assert.Equal(glaetten, Clean(glaetten, raw: "ähm okay hier ist der neue Text äh liebe Frau Müller ich komme am Montag"));
        Assert.Equal("- Angebot schicken", Clean("Hier ist die Zusammenfassung:\n- Angebot schicken", raw: "wir schicken das Angebot"));
    }

    /// <summary>Wie oben, aber mit der echten Füllwort-Liste aus WLD-P1a (Kotlin: TextPolisher.builtinFillers de + en).</summary>
    [Fact]
    public void DiktierteEinleitungMitFuellsilbenDerEchtenListe()
    {
        const string glaetten = "Okay, hier ist der neue Text:\nLiebe Frau Müller,\nich komme am Montag.";
        var cleanup = TextCleanup.For(CoreText.Polisher);
        Assert.Equal(glaetten, cleanup.CleanText("ähm okay hier ist der neue Text äh liebe Frau Müller ich komme am Montag", glaetten));
    }

    [Fact]
    public void AnfuehrungszeichenUmDenGanzenTextFallenWeg()
    {
        Assert.Equal("Ich kann morgen nicht.", Clean("„Ich kann morgen nicht.“"));
        Assert.Equal("Ich kann morgen nicht.", Clean("\"Ich kann morgen nicht.\""));
        Assert.Equal("Ich kann morgen nicht.", Clean("```\nIch kann morgen nicht.\n```"));
        const string zitat = "Sie sagte „morgen“ und ging.";
        Assert.Equal(zitat, Clean(zitat, raw: "sie sagte morgen und ging"));
    }

    [Fact]
    public void DiktatImZitatBehaeltSeineAnfuehrungszeichen() =>
        Assert.Equal("„Ich komme gleich.“", Clean("„Ich komme gleich.“", raw: "„Ich komme gleich.“"));

    [Fact]
    public void ErfuellteBitteWirdAbgelehnt()
    {
        const string raw = "schreib mir bitte ne kurze Einladung für meinen Geburtstag am Samstag";
        var einladung = string.Join(" ", Enumerable.Repeat("Ihr seid herzlich eingeladen, mit mir am Samstag zu feiern.", 8));
        var e = Assert.Throws<RefineRejectedException>(() => Clean(einladung, raw));
        Assert.Contains("statt den Text zu bearbeiten", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void KurzeAntwortAufKurzesDiktatBleibtImRahmen()
    {
        Assert.Equal("Ja.", Clean("Ja.", raw: "Äh, ähm, ja."));
        Assert.Equal("Okay, bin in zehn Minuten da.", Clean("Okay, bin in zehn Minuten da.", raw: "okay bin in zehn minuten da"));
    }

    /// <summary>Desktop: Wortzählung und „kurz“ wie RefinePrompt.wordCount/isShort (Grenze 15 Wörter).</summary>
    [Fact]
    public void WortzaehlungUndKurzesDiktat()
    {
        Assert.Equal(3, TextCleanup.WordCount("  eins\tzwei\ndrei "));
        Assert.Equal(0, TextCleanup.WordCount("   "));
        Assert.True(TextCleanup.IsShort(string.Join(" ", Enumerable.Repeat("w", 14))));
        Assert.False(TextCleanup.IsShort(string.Join(" ", Enumerable.Repeat("w", 15))));
    }
}
