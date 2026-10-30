using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Tests.Text;

/// <summary>
/// Port von <c>TextPolisherTest.kt</c>: Füllwort-Entfernung, Groß-Schreibung, Whitespace- und
/// Satzzeichen-Normalisierung. Testvektoren 1:1.
/// </summary>
public class TextPolisherTest
{
    private static readonly TextPolisher Polisher = new();

    private static readonly PolishOptions Full = new(RemoveFillers: true, AutoCapitalize: true, Language: "auto");

    private static string Polish(string raw, PolishOptions options) => Polisher.Polish(raw, options);

    [Fact]
    public void LeererInputBleibtLeer()
    {
        Assert.Equal("", Polish("", Full));
        Assert.Equal("", Polish("   \n  ", Full));
    }

    [Fact]
    public void WhitespaceWirdNormalisiert()
    {
        // Nur Satzanfang groß (kein Title-Case) -> "welt" bleibt klein.
        Assert.Equal("Hallo welt", Polish("  hallo   welt  ", Full));
        Assert.Equal("Hallo welt", Polish("hallo\n\twelt", Full));
    }

    [Fact]
    public void ErsterBuchstabeGross()
    {
        Assert.Equal("Das ist ein Test.", Polish("das ist ein Test.", Full));
    }

    [Fact]
    public void GrossNachSatzende()
    {
        Assert.Equal("Hallo. Wie geht's? Gut!", Polish("hallo. wie geht's? gut!", Full));
    }

    // --- Satzende nur mit Leerraum danach (Regression, Stufe "Prompt") --------------------

    [Fact]
    public void PunktInZahlenUndDateinamenBeendetKeinenSatz()
    {
        Assert.Equal(
            "Was ist neu in Python 3.13 gegenüber 3.12?",
            Polish("was ist neu in Python 3.13 gegenüber 3.12?", Full));
        Assert.Equal("Lohnt sich GPT 4.1 mini", Polish("lohnt sich GPT 4.1 mini", Full));
        Assert.Equal("Öffne config.yaml und www.example.com", Polish("öffne config.yaml und www.example.com", Full));
    }

    [Fact]
    public void SatzendeMitAnfuehrungszeichenOderKlammerZaehltWeiter()
    {
        Assert.Equal("Er sagte „Stopp.“ Dann ging er.", Polish("er sagte „Stopp.“ dann ging er.", Full));
        Assert.Equal("(Siehe oben.) Weiter geht's.", Polish("(siehe oben.) weiter geht's.", Full));
        // Review-Befund: auch Emojis, Sternchen und das deutsche ‚…‘ stehen zwischen Satzende und Leerraum.
        Assert.Equal("Super!😀 Danke", Polish("super!😀 danke", Full));
        Assert.Equal("**Wichtig.** Danach", Polish("**wichtig.** danach", Full));
        Assert.Equal("Er sagte ‚Stopp.‘ Dann", Polish("er sagte ‚Stopp.‘ dann", Full));
    }

    [Fact]
    public void ZeilenanfangMitZifferBleibtKlein()
    {
        var lines = Full with { KeepLineBreaks = true };
        Assert.Equal(
            "Make me a plan.\n- 12 people, 2 of them vegan",
            Polish("make me a plan.\n- 12 people, 2 of them vegan", lines));
        Assert.Equal("Hallo.\nWelt", Polish("hallo.\nwelt", lines));
    }

    // --- Abkürzungen beenden keinen Satz (Nebenbefund N1) ----------------------------------

    [Fact]
    public void NachAbkuerzungUndOrdnungszahlBleibtKlein()
    {
        Assert.Equal("Obst, z. B. ein Apfel.", Polish("obst, z. B. ein Apfel.", Full));
        Assert.Equal("Das kostet ca. fünf Euro.", Polish("das kostet ca. fünf Euro.", Full));
        Assert.Equal("Das heißt d.h. nichts.", Polish("das heißt d.h. nichts.", Full));
        Assert.Equal("Vom 1. bis 5. Mai", Polish("vom 1. bis 5. Mai", Full));
    }

    [Fact]
    public void NachDerKiBleibtKleinNachAbkuerzung()
    {
        var afterAi = PolishPlan.Options(removeFillers: true, autoCapitalize: true, language: "de", refineMode: RefineMode.Polish);
        Assert.Equal("Bring z. B. ein Brot mit.", Polish("Bring z. B. ein Brot mit.", afterAi));
        Assert.Equal("Wir sind ca. zehn Leute.", Polish("Wir sind ca. zehn Leute.", afterAi));
    }

    [Fact]
    public void SatzendeNachEchtemSatzBleibtGross()
    {
        Assert.Equal("Das ist gut. Das auch.", Polish("das ist gut. das auch.", Full));
        Assert.Equal("Wir waren im Kino. Danach", Polish("wir waren im Kino. ähm danach", Full));
        Assert.Equal("Er ging (z. B. heim). Dann", Polish("er ging (z. B. heim). dann", Full));
    }

    // --- Ein entferntes Füllwort am Satzanfang gibt die Groß-Schreibung weiter (Review 3.9.0) ----

    [Fact]
    public void NachZahlOderEinzelbuchstabeAmSatzendeUebernimmtDasFolgewortDieGrossSchreibung()
    {
        // Die Abkürzungs-Regel (N1) hält "2021." und "B." für kein Satzende — das groß
        // geschriebene "Ähm" stand aber am Satzanfang, also gilt das für das Wort danach.
        Assert.Equal(
            "Das Projekt begann 2021. Dann kam die Pandemie.",
            Polish("Das Projekt begann 2021. Ähm, dann kam die Pandemie.", Full));
        Assert.Equal("Plan B. Also los.", Polish("Plan B. Ähm, also los.", Full));
        Assert.Equal(
            "We met in room 204. Then we left.",
            Polish("We met in room 204. Um, then we left.", new PolishOptions(Language: "en")));
    }

    [Fact]
    public void NachDerKiUebernimmtDasFolgewortDieGrossSchreibung()
    {
        // Werkseinstellung: Glätten · Nur Zeichensetzung lässt jedes "Ähm" stehen, die Liste räumt danach auf.
        var afterAi = PolishPlan.Options(removeFillers: true, autoCapitalize: true, language: "de", refineMode: RefineMode.Polish);
        Assert.Equal(
            "Das Projekt begann 2021. Dann kam die Pandemie.",
            Polish("Das Projekt begann 2021. Ähm, dann kam die Pandemie.", afterAi));
        Assert.Equal("Zimmer 12. Bring Brot mit.", Polish("Zimmer 12. Äh, bring Brot mit.", afterAi));
    }

    [Fact]
    public void MehrereFuellwoerterAmSatzanfangGebenDieGrossSchreibungWeiter()
    {
        Assert.Equal("Das war 2021. Dann kam X.", Polish("Das war 2021. Ähm, äh, dann kam X.", Full));
        Assert.Equal("Dann kam X.", Polish("Ähm, äh, dann kam X.", new PolishOptions(Language: "de", AutoCapitalize: false)));
    }

    [Fact]
    public void GrossesFuellwortMittenImSatzMachtNichtsGross()
    {
        Assert.Equal("Er sagte nein.", Polish("er sagte Ähm, nein.", Full));
        Assert.Equal("Vom 1. bis 5. Mai", Polish("vom 1. ähm bis 5. Mai", Full));
    }

    [Fact]
    public void PunktVorWortBleibtVomVorwortGetrennt()
    {
        Assert.Equal(
            "Lösche alle .log-Dateien und die Datei .env.",
            Polish("lösche alle .log-Dateien und die Datei .env .", Full));
        Assert.Equal("Ende. Hallo, welt!", Polish("ende . hallo , welt !", Full));
    }

    [Fact]
    public void DeutscheFuellwoerterEntfernt()
    {
        Assert.Equal("Ich denke das ist gut.", Polish("ich ähm denke äh das ist gut.", new PolishOptions(Language: "de")));
    }

    [Fact]
    public void FuellwortGrossgeschriebenAmSatzanfangEntfernt()
    {
        // "Ähm" am Anfang muss weg, danach wird korrekt groß geschrieben.
        Assert.Equal("Also gut.", Polish("Ähm also gut.", new PolishOptions(Language: "de")));
    }

    [Fact]
    public void EnglischeFuellwoerterEntfernt()
    {
        Assert.Equal("I think this works.", Polish("i um think uh this works.", new PolishOptions(Language: "en")));
    }

    [Fact]
    public void EchteWoerterBleibenErhalten()
    {
        // "um" ist hier Teilstring von "umsonst" / eigenständiges dt. Wort -> nicht anfassen bei de.
        Assert.Equal("Das war umsonst.", Polish("das war umsonst.", new PolishOptions(Language: "de")));
    }

    [Fact]
    public void FuellwortInWortGrenzeNichtEntfernt()
    {
        // "erm" darf nicht aus "Determinante" gerissen werden.
        Assert.Equal("Determinante", Polish("Determinante", new PolishOptions(Language: "en")));
    }

    [Fact]
    public void LeerzeichenVorSatzzeichenEntfernt()
    {
        Assert.Equal("Hallo, welt!", Polish("hallo , welt !", Full));
    }

    [Fact]
    public void FuellwortEntfernungAbschaltbar()
    {
        Assert.Equal("Ich ähm denke.", Polish("ich ähm denke.", new PolishOptions(RemoveFillers: false, Language: "de")));
    }

    [Fact]
    public void GrossSchreibungAbschaltbar()
    {
        Assert.Equal("das bleibt klein.", Polish("das bleibt klein.", new PolishOptions(AutoCapitalize: false)));
    }

    [Fact]
    public void AutoModusLoeschtKeineEchtenWoerter()
    {
        // "um" (dt.) und "este" (span.) sind echte Wörter -> im auto-Modus nicht entfernen.
        Assert.Equal("Ich gehe um die Ecke.", Polish("ich gehe um die Ecke.", new PolishOptions(Language: "auto")));
        Assert.Equal("Compré este libro.", Polish("compré este libro.", new PolishOptions(Language: "auto")));
    }

    [Fact]
    public void AutoModusEntferntEindeutigeFueller()
    {
        Assert.Equal("Ich denke ja.", Polish("ich ähm denke uh ja.", new PolishOptions(Language: "auto")));
    }

    // --- eigene und abgewählte Füllwörter -----------------------------------

    [Fact]
    public void EigeneFuellwoerterWerdenEntfernt()
    {
        Assert.Equal(
            "Das ist gut.",
            Polish("das ist halt gut, sozusagen.", new PolishOptions(Language: "de", CustomFillers: ["halt", "sozusagen"])));
    }

    [Fact]
    public void FuellwortVorSatzendeLaesstKeinKommaZurueck()
    {
        Assert.Equal("Das ist gut.", Polish("das ist gut, ähm.", new PolishOptions(Language: "de")));
        Assert.Equal("Echt, ja?", Polish("echt, ähm, ja?", new PolishOptions(Language: "de")));
        // Ohne Füllwort-Entfernung bleibt der Wortlaut samt Kommas unangetastet.
        Assert.Equal("Gut, ähm.", Polish("gut, ähm.", new PolishOptions(RemoveFillers: false, Language: "de")));
    }

    [Fact]
    public void EigeneFuellwoerterNurAlsGanzeWoerterUndOhneGrossKlein()
    {
        var o = new PolishOptions(Language: "de", CustomFillers: ["halt"]);
        Assert.Equal("Haltestelle bleibt.", Polish("Haltestelle bleibt.", o));
        Assert.Equal("Das bleibt.", Polish("das HALT bleibt.", o));
        // Unicode: Wortgrenze vor/nach Umlauten.
        Assert.Equal("Schön so.", Polish("schön äh so.", new PolishOptions(Language: "de", CustomFillers: ["äh"])));
    }

    [Fact]
    public void LeereEigeneEintraegeWerdenIgnoriert()
    {
        Assert.Equal("Das bleibt.", Polish("das bleibt.", new PolishOptions(Language: "de", CustomFillers: ["", "   "])));
    }

    [Fact]
    public void MehrwortFuellerFunktionieren()
    {
        Assert.Equal(
            "I think so.",
            Polish("I think you know so.", new PolishOptions(Language: "en", CustomFillers: ["you know"])));
    }

    [Fact]
    public void AbgewaehlteStandardwoerterBleibenStehen()
    {
        Assert.Equal(
            "Ich hmm denke ja.",
            Polish("ich hmm denke ähm ja.", new PolishOptions(Language: "de", DisabledFillers: new HashSet<string> { "hmm" })));
    }

    [Fact]
    public void EingebauteListenSindOeffentlich()
    {
        Assert.Contains("ähm", Polisher.BuiltinFillers("de"));
        Assert.Contains("um", Polisher.BuiltinFillers("en"));
        Assert.Contains("ähm", Polisher.BuiltinFillers("auto"));
        Assert.DoesNotContain("um", Polisher.BuiltinFillers("auto"));
        Assert.Empty(Polisher.BuiltinFillers("xx"));
        foreach (var lang in new[] { "de", "en", "es", "fr", "it", "auto" })
        {
            foreach (var w in Polisher.BuiltinFillers(lang))
            {
                Assert.Equal(w, w.ToLowerInvariant());
            }
        }
    }

    // --- Zeilenumbrüche ----------------------------------------------------------

    [Fact]
    public void KiAbsaetzeBleibenErhalten()
    {
        var o = new PolishOptions(Language: "de", KeepLineBreaks: true);
        Assert.Equal("Erster Absatz.\n\nZweiter Absatz.", Polish("Erster   Absatz. \n\n\n\n Zweiter Absatz.  ", o));
        Assert.Equal("- Punkt eins\n- Punkt zwei", Polish("- Punkt eins\n- Punkt zwei", o));
    }

    [Fact]
    public void FuellwortAmZeilenendeLaesstKeinenRest()
    {
        var o = new PolishOptions(Language: "de", KeepLineBreaks: true);
        Assert.Equal("Ich denke.\nJa.", Polish("ich denke ähm.\näh ja.", o));
    }
}
