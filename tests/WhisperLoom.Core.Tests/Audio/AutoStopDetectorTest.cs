using WhisperLoom.Core.Audio;
using Decision = WhisperLoom.Core.Audio.AutoStopDetector.Decision;

namespace WhisperLoom.Core.Tests.Audio;

/// <summary>
/// Port von <c>agent/AutoStopDetectorTest.kt</c>. Ein Verlauf ist eine Folge von Puffern (Zeit, Peak);
/// die Zeit kommt von außen, deshalb ist jeder Fall exakt und ohne Warten prüfbar.
/// </summary>
public class AutoStopDetectorTest
{
    private const float Stille = 0.01f;

    /// <summary>Erste Entscheidung außer Continue mit ihrem Zeitpunkt, oder Continue mit -1.</summary>
    private static (Decision, long) ErsteEntscheidung(AutoStopDetector d, IEnumerable<(long At, float Peak)> frames)
    {
        foreach (var (at, peak) in frames)
        {
            var e = d.Feed(peak, at);
            if (e != Decision.Continue)
            {
                return (e, at);
            }
        }
        return (Decision.Continue, -1L);
    }

    /// <summary>40-ms-Puffer von 0 bis <paramref name="bisMs"/> (ausschließlich), Pegel je Zeitpunkt.</summary>
    private static List<(long At, float Peak)> Verlauf(long bisMs, Func<long, float> pegel, long schritt = 40)
    {
        var frames = new List<(long, float)>();
        for (var t = 0L; t < bisMs; t += schritt)
        {
            frames.Add((t, pegel(t)));
        }
        return frames;
    }

    /// <summary>Silben: laut und leiser im Wechsel, aber ohne Lücke bis zur Stille.</summary>
    private static float Silbe(long t) => t / 120 % 2 == 0L ? 0.4f : 0.12f;

    private static bool In(long t, long from, long until) => t >= from && t < until;

    // --- Sprechpause ---------------------------------------------------------

    [Fact]
    public void StopptGenauEineSprechpauseNachDemEnde()
    {
        // Sprache von 0,5 s bis 3 s, danach Stille: Stopp bei 3000 + 2000, keinen Puffer früher.
        var frames = Verlauf(10_000, t => In(t, 500, 3_000) ? Silbe(t) : Stille);
        Assert.Equal((Decision.SpeechEnded, 5_000L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void EineAtempauseStopptNicht()
    {
        // 1,2 s Luft holen mitten im Diktat: die Aufnahme läuft weiter bis zum echten Ende.
        var frames = Verlauf(12_000, t => In(t, 500, 2_500) || In(t, 3_700, 5_000) ? Silbe(t) : Stille);
        Assert.Equal((Decision.SpeechEnded, 7_000L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void DiePauseKommtAusDemProfil()
    {
        // Sprache 0,5-3 s: Stopp je nach gewählter Sprechpause (20-ms-Puffer treffen 6,5 s genau).
        var frames = Verlauf(12_000, t => In(t, 500, 3_000) ? Silbe(t) : Stille, schritt: 20);
        var erwartet = new Dictionary<SpeechPause, long>
        {
            [SpeechPause.Short] = 4_200L,
            [SpeechPause.Normal] = 5_000L,
            [SpeechPause.Long] = 6_500L,
        };
        foreach (var (pause, stopp) in erwartet)
        {
            Assert.Equal((Decision.SpeechEnded, stopp), ErsteEntscheidung(new AutoStopDetector(pause.Ms()), frames));
        }
    }

    [Fact]
    public void VorDerMindestdauerKommtKeinStopp()
    {
        // Kurzer Satz mit sehr kurzer Pause: frei wäre bei 600 ms, gestoppt wird erst bei 1500.
        var frames = Verlauf(4_000, t => t < 400 ? 0.4f : Stille, schritt: 50);
        Assert.Equal(
            (Decision.SpeechEnded, AutoStopDetector.MinRecordingMs),
            ErsteEntscheidung(new AutoStopDetector(pauseMs: 200), frames));
    }

    [Fact]
    public void UnregelmaessigePufferWerdenVertragen()
    {
        // Puffer mal 20, mal 120 ms: gestoppt wird am ersten Puffer, der 2 s nach dem Ende liegt.
        long[] abstaende = [20, 90, 45, 120, 60];
        var frames = new List<(long At, float Peak)>();
        var t = 0L;
        var i = 0;
        while (t < 10_000)
        {
            frames.Add((t, In(t, 500, 3_000) ? 0.4f : Stille));
            t += abstaende[i++ % abstaende.Length];
        }
        var endeDerSprache = frames.First(f => f.At >= 3_000).At;
        var erwartet = frames.First(f => f.At >= endeDerSprache + 2_000).At;
        Assert.Equal((Decision.SpeechEnded, erwartet), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void DieEntscheidungRastetEin()
    {
        var stopp = new AutoStopDetector();
        ErsteEntscheidung(stopp, Verlauf(6_000, t => t < 1_000 ? 0.4f : Stille));
        Assert.Equal(Decision.SpeechEnded, stopp.Feed(0.5f, 6_000)); // Wer danach spricht, ändert nichts

        var nichts = new AutoStopDetector();
        ErsteEntscheidung(nichts, Verlauf(9_000, _ => Stille));
        Assert.Equal(Decision.NoSpeech, nichts.Feed(0.5f, 9_000));
        Assert.Equal(Decision.NoSpeech, nichts.Feed(0.5f, 9_500));
    }

    // --- Keine Sprache -------------------------------------------------------

    [Fact]
    public void NurStilleIstNachAchtSekundenNichtsGehoert()
    {
        var frames = Verlauf(10_000, _ => Stille);
        Assert.Equal((Decision.NoSpeech, AutoStopDetector.NoSpeechMs), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void EinKlickIstKeineSprache()
    {
        // 100 ms mit 0,8 (Klopfen ans Telefon): danach bleibt es bei "nichts gehört".
        var frames = Verlauf(10_000, t => In(t, 2_000, 2_100) ? 0.8f : Stille);
        Assert.Equal((Decision.NoSpeech, AutoStopDetector.NoSpeechMs), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void WerKurzVorAblaufLossprichtWirdNichtAbgeschnitten()
    {
        // Sprache ab 7,9 s: bei 8 s ist der laute Abschnitt noch nicht bestätigt, aber im Gange.
        var frames = Verlauf(14_000, t => In(t, 7_900, 9_500) ? 0.4f : Stille);
        Assert.Equal((Decision.SpeechEnded, 11_520L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    // --- Regression: Rauschboden (fatal flaw eines Entwurfs) ------------------

    [Fact]
    public void WerAbNullSprichtVerliertNichts()
    {
        // Dauerton 0,4 für 3 s ab dem ersten Puffer: ein aus den ersten Puffern gelernter Boden
        // hätte die Stimme selbst zum Hintergrund erklärt — und nach 8 s "nichts gehört".
        var frames = Verlauf(10_000, t => t < 3_000 ? 0.4f : Stille);
        Assert.Equal((Decision.SpeechEnded, 5_000L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void FuenfSekundenDurchgehendeSpracheHaltenDurch()
    {
        var d = new AutoStopDetector();
        foreach (var (at, peak) in Verlauf(5_000, _ => 0.3f))
        {
            Assert.Equal(Decision.Continue, d.Feed(peak, at));
        }
    }

    [Fact]
    public void RauschenPlusSpracheStopptErstNachDemEnde()
    {
        // Grundrauschen 0,08 durchgehend, Sprache 0,4 von 0,5 bis 4 s: Stopp bei 6 s, nicht vorher.
        var frames = Verlauf(12_000, t => In(t, 500, 4_000) ? 0.4f : 0.08f);
        Assert.Equal((Decision.SpeechEnded, 6_000L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void DigitaleNullenFrierenDenBodenNichtEin()
    {
        // Viele Mikrofone liefern beim Anlaufen reine Nullen. Ohne Untergrenze bliebe der Boden
        // danach 0 (er steigt multiplikativ) — ein Dauerbrummen würde nie zum Hintergrund.
        var frames = Verlauf(20_000, t => t < 500 ? 0f : 0.08f);
        Assert.Equal((Decision.NoSpeech, AutoStopDetector.NoSpeechMs), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    // --- Regression: Dauerlärm ist keine Sprache ------------------------------

    [Fact]
    public void DauerrauschenOhneSpracheIstNichtsGehoert()
    {
        // Auto, Straße, Lüfter: gleich ab dem ersten Puffer über der Schwelle und lange laut —
        // der Abschnitt endet aber nur, weil der Boden ihn einholt. Gesendet werden darf das nie.
        foreach (var pegel in new[] { 0.05f, 0.08f, 0.12f, 0.2f })
        {
            var frames = Verlauf(12_000, _ => pegel);
            Assert.Equal((Decision.NoSpeech, AutoStopDetector.NoSpeechMs), ErsteEntscheidung(new AutoStopDetector(), frames));
        }
    }

    [Fact]
    public void SchwankendesRauschenOhneSpracheIstNichtsGehoert()
    {
        var zufall = new KotlinRandom(42);
        var frames = Verlauf(12_000, _ => 0.06f + 0.04f * zufall.NextFloat());
        Assert.Equal((Decision.NoSpeech, AutoStopDetector.NoSpeechMs), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void LautesBrummenOhneSpracheIstNichtsGehoert()
    {
        // 0,4 bleibt bis ~7,4 s laut, 1,0 bis ~10 s: bewertet wird erst am Ende des Abschnitts.
        Assert.Equal(
            (Decision.NoSpeech, AutoStopDetector.NoSpeechMs),
            ErsteEntscheidung(new AutoStopDetector(), Verlauf(12_000, _ => 0.4f)));
        var (e, at) = ErsteEntscheidung(new AutoStopDetector(), Verlauf(15_000, _ => 1f));
        Assert.Equal(Decision.NoSpeech, e);
        Assert.True(at > AutoStopDetector.NoSpeechMs, $"erst nach dem lauten Abschnitt: {at}");
    }

    [Fact]
    public void SpracheNachDemDauerrauschenWirdErkannt()
    {
        // Dauerrauschen 0,08 ab dem Start, gesprochen wird erst ab 4 s: drei Worte mit kurzen
        // Lücken bis 5,4 s. Der Rauschabschnitt davor zählt nicht, die Worte schon.
        (long From, long Until)[] worte = [(4_000, 4_400), (4_500, 4_900), (5_000, 5_400)];
        var frames = Verlauf(14_000, t => worte.Any(w => In(t, w.From, w.Until)) ? 0.4f : 0.08f);
        Assert.Equal((Decision.SpeechEnded, 7_400L), ErsteEntscheidung(new AutoStopDetector(), frames));
    }

    [Fact]
    public void DerBodenFaelltSofortUndSteigtGebremst()
    {
        var d = new AutoStopDetector();
        d.Feed(0.5f, 0);
        Assert.Equal(AutoStopDetector.FloorInit, d.Floor, 1e-6f); // Start fest, nicht aus dem ersten Puffer gelernt
        d.Feed(0.5f, 1_000);
        // 3 dB/s: nach einer Sekunde Faktor 10^(3/20) = 1,4125
        Assert.Equal(AutoStopDetector.FloorInit * 1.4125f, d.Floor, 1e-4f);
        d.Feed(0.014f, 1_040);
        Assert.Equal(0.014f, d.Floor, 1e-6f); // Minimum-Folger: fällt sofort
    }

    // --- Desktop: Sprechpause aus dem Profil ------------------------------------------------

    [Fact]
    public void SprechpauseSchluesselUndDauer()
    {
        Assert.Equal(["short", "normal", "long"], Enum.GetValues<SpeechPause>().Select(p => p.Key()));
        Assert.Equal([1_200L, 2_000L, 3_500L], Enum.GetValues<SpeechPause>().Select(p => p.Ms()));
        Assert.Equal(SpeechPause.Long, SpeechPauses.FromKey("long"));
        Assert.Equal(SpeechPause.Normal, SpeechPauses.FromKey("zaubern"));
        Assert.Equal(SpeechPause.Normal, SpeechPauses.FromKey(null));
        Assert.Equal(SpeechPause.Normal.Ms(), new AutoStopDetector().PauseMs);
    }
}

/// <summary>
/// Kotlins <c>Random(seed)</c> (XorWow, kotlin.random.XorWowRandom), damit der Rausch-Verlauf aus
/// <c>schwankendesRauschenOhneSpracheIstNichtsGehoert</c> 1:1 derselbe ist.
/// </summary>
internal sealed class KotlinRandom
{
    private int x;
    private int y;
    private int z;
    private int w;
    private int v;
    private int addend;

    public KotlinRandom(int seed)
    {
        var seed2 = seed >> 31;
        x = seed;
        y = seed2;
        z = 0;
        w = 0;
        v = ~seed;
        addend = (seed << 10) ^ (int)((uint)seed2 >> 4);
        for (var i = 0; i < 64; i++)
        {
            NextInt();
        }
    }

    public int NextInt()
    {
        var t = x;
        t ^= (int)((uint)t >> 2);
        x = y;
        y = z;
        z = w;
        var v0 = v;
        w = v0;
        t = t ^ (t << 1) ^ v0 ^ (v0 << 4);
        v = t;
        addend += 362437;
        return t + addend;
    }

    /// <summary>nextBits(24) / 2^24, wie <c>Random.nextFloat()</c>.</summary>
    public float NextFloat() => (int)((uint)NextInt() >> 8) / (float)(1 << 24);
}
