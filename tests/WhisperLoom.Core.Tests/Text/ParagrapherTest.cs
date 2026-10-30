using System.Text.RegularExpressions;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Tests.Text;

/// <summary>Port von <c>ParagrapherTest.kt</c>: die Absatz-Heuristik (UX-Spec §2.9).</summary>
public class ParagrapherTest
{
    private static string Normalized(string s) => Regex.Replace(s.Trim(), @"\s+", " ");

    private static string Sentence(int i) => $"Das ist der ganz normale Satz Nummer {i} mit ein paar Woertern drin.";

    private static string Sentences(int from, int to, string separator = " ") =>
        string.Join(separator, Enumerable.Range(from, to - from + 1).Select(Sentence));

    [Fact]
    public void LeerBleibtLeer()
    {
        Assert.Empty(Paragrapher.Split(""));
        Assert.Empty(Paragrapher.Split("   \n "));
    }

    [Fact]
    public void KurzerTextIstEinAbsatz()
    {
        const string t = "Hallo. Wie geht es dir? Mir geht es gut.";
        Assert.Equal([t], Paragrapher.Split(t));
    }

    [Fact]
    public void LangerTextWirdGeteiltUndWoerterBleibenUnveraendert()
    {
        var text = Sentences(1, 14, "  ");
        var paragraphs = Paragrapher.Split(text);
        Assert.True(paragraphs.Count >= 2, $"erwartet mehrere Absätze, war {paragraphs.Count}");
        Assert.Equal(Normalized(text), Normalized(string.Join(" ", paragraphs)));
        foreach (var p in paragraphs)
        {
            Assert.True(p.EndsWith('.'), $"Absatz endet mitten im Satz: {p}");
            Assert.True(p.Length <= 600, $"Absatz zu lang: {p.Length}");
        }
    }

    [Fact]
    public void AbsatzwechselBevorzugtVorDiskursmarker()
    {
        // Nach ~300 Zeichen kommt "Außerdem …" — dort soll der Absatz beginnen.
        var text = Sentences(1, 5) + " Außerdem wollte ich noch etwas sagen. " + Sentences(6, 9);
        var paragraphs = Paragrapher.Split(text);
        Assert.Contains(paragraphs, p => p.StartsWith("Außerdem", StringComparison.Ordinal));
        Assert.Equal(Normalized(text), Normalized(string.Join(" ", paragraphs)));
    }

    [Fact]
    public void AbkuerzungenUndOrdnungszahlenTrennenKeinenSatz()
    {
        var s = Paragrapher.Sentences("Wir treffen uns am 3. Oktober, z. B. um 10 Uhr bzw. spaeter. Danach essen wir.");
        Assert.Equal(["Wir treffen uns am 3. Oktober, z. B. um 10 Uhr bzw. spaeter.", "Danach essen wir."], s);
        Assert.Equal(
            ["Das gilt z.B. hier, d.h. ueberall.", "Danach essen wir."],
            Paragrapher.Sentences("Das gilt z.B. hier, d.h. ueberall. Danach essen wir."));
    }

    [Fact]
    public void SatzgrenzenMitVerschiedenenZeichen()
    {
        Assert.Equal(["Echt?", "Ja!", "Okay…", "Gut."], Paragrapher.Sentences("Echt? Ja! Okay… Gut."));
    }

    [Fact]
    public void KurzerSchlusssatzHaengtSichAn()
    {
        var text = Sentences(1, 8) + " Tschüss.";
        var paragraphs = Paragrapher.Split(text);
        Assert.EndsWith("Tschüss.", paragraphs[^1]);
        Assert.True(paragraphs[^1].Length > "Tschüss.".Length);
    }

    // UX-Spec §2.9: nach 3 Sätzen beginnt ein neuer Absatz …
    [Fact]
    public void HoechstensDreiSaetzeProAbsatz()
    {
        var text = Sentences(1, 7);
        var paragraphs = Paragrapher.Split(text);
        Assert.Equal([3, 3, 1], paragraphs.Select(p => Paragrapher.Sentences(p).Count));
        Assert.Equal(Normalized(text), Normalized(string.Join(" ", paragraphs)));
    }

    // … oder sobald 350 Zeichen erreicht sind: zwei 200-Zeichen-Sätze reichen.
    [Fact]
    public void AbsatzEndetSobald350ZeichenErreichtSind()
    {
        var longSentence = string.Concat(Enumerable.Repeat("Wort ", 40)).Trim() + ".";
        Assert.Equal(200, longSentence.Length);
        var paragraphs = Paragrapher.Split(string.Join(" ", Enumerable.Repeat(longSentence, 4)));
        Assert.Equal([2, 2], paragraphs.Select(p => Paragrapher.Sentences(p).Count));
    }

    // Bewusste Ausnahme: ein einzelner kurzer Schlusssatz bleibt nicht allein stehen.
    [Fact]
    public void EinzelnerKurzerSchlusssatzWirdAngehaengt()
    {
        var text = Sentences(1, 9) + " Tschüss.";
        var paragraphs = Paragrapher.Split(text);
        Assert.Equal(3, paragraphs.Count);
        Assert.EndsWith("Tschüss.", paragraphs[^1]);
        Assert.Equal(Normalized(text), Normalized(string.Join(" ", paragraphs)));
    }

    [Fact]
    public void OhneSatzzeichenBleibtEinBlock()
    {
        var words = string.Join(" ", Enumerable.Range(1, 200).Select(i => $"wort{i}"));
        Assert.Equal([words], Paragrapher.Split(words));
    }
}
