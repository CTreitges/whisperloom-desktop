namespace WhisperLoom.Core.Audio;

/// <summary>
/// Entscheidet aus dem Pegelverlauf einer Aufnahme, wann sie von selbst endet (Auto-Stopp). Port von
/// <c>agent/AutoStopDetector.kt</c>. Rein und ohne eigene Uhr: der Aufrufer reicht je Puffer den Peak
/// (0..1) und die Zeit seit Aufnahmebeginn herein — so ist jeder Verlauf im Test exakt nachstellbar.
/// <list type="bullet">
/// <item>Rauschboden als Minimum-Folger: fällt sofort auf einen leiseren Puffer, steigt höchstens um
/// <see cref="FloorRiseDbPerS"/> dB/s. Er startet FEST bei <see cref="FloorInit"/> — wer gleich losspricht,
/// würde sonst selbst zum Boden.</item>
/// <item>Laut ist ein Puffer über <c>on = max(minThreshold, factor * Boden)</c>; innerhalb eines lauten
/// Abschnitts reicht <c>off = hysteresis * on</c>.</item>
/// <item>Sprache gilt als erkannt, wenn ein lauter Abschnitt mindestens <see cref="SpeechConfirmMs"/> dauert UND
/// mit einem echten Pegelabfall endet (unter <see cref="SpeechDrop"/> mal dem lautesten Puffer). Dauerlärm
/// endet nur, weil der steigende Boden ihn einholt — der Pegel fällt nicht.</item>
/// <item><see cref="Decision.SpeechEnded"/>: nach erkannter Sprache <see cref="PauseMs"/> lang leise,
/// frühestens nach <see cref="MinRecordingMs"/>.</item>
/// <item><see cref="Decision.NoSpeech"/>: bis <see cref="NoSpeechMs"/> keine Sprache erkannt; ein laufender
/// lauter Abschnitt wird erst zu Ende bewertet.</item>
/// </list>
/// Die erste Entscheidung außer <see cref="Decision.Continue"/> rastet ein. Nicht threadsicher: ein Detektor
/// gehört genau einer Aufnahme.
/// </summary>
public sealed class AutoStopDetector
{
    /// <summary>Start-Rauschboden, fest (nicht gelernt).</summary>
    public const float FloorInit = 0.015f;

    public const float FloorRiseDbPerS = 3f;
    public const float MinThreshold = 0.04f;
    public const float Factor = 3f;
    public const float Hysteresis = 0.7f;
    public const long SpeechConfirmMs = 250L;

    /// <summary>Sprache endet mit einem Abfall um mindestens 6 dB unter den lautesten Puffer.</summary>
    public const float SpeechDrop = 0.5f;

    public const long MinRecordingMs = 1_500L;
    public const long NoSpeechMs = 8_000L;

    private readonly float floorRiseDbPerS;
    private readonly float minThreshold;
    private readonly float factor;
    private readonly float hysteresis;
    private readonly long speechConfirmMs;
    private readonly float speechDrop;
    private readonly long minRecordingMs;
    private readonly long noSpeechMs;
    private readonly float floorMin;

    private long lastAt = -1L;
    private bool loud;
    private long loudSince;
    private long quietSince;

    /// <summary>Lautester Puffer und "lang genug" des laufenden lauten Abschnitts.</summary>
    private float sectionPeak;

    private bool sectionLong;
    private bool speech;
    private Decision decision = Decision.Continue;

    /// <param name="pauseMs">Stille nach erkannter Sprache bis zum Stopp; kommt aus dem Profil (<see cref="SpeechPause"/>).</param>
    public AutoStopDetector(
        long pauseMs = 2_000L,
        float floorInit = FloorInit,
        float floorRiseDbPerS = FloorRiseDbPerS,
        float minThreshold = MinThreshold,
        float factor = Factor,
        float hysteresis = Hysteresis,
        long speechConfirmMs = SpeechConfirmMs,
        float speechDrop = SpeechDrop,
        long minRecordingMs = MinRecordingMs,
        long noSpeechMs = NoSpeechMs)
    {
        PauseMs = pauseMs;
        Floor = floorInit;
        this.floorRiseDbPerS = floorRiseDbPerS;
        this.minThreshold = minThreshold;
        this.factor = factor;
        this.hysteresis = hysteresis;
        this.speechConfirmMs = speechConfirmMs;
        this.speechDrop = speechDrop;
        this.minRecordingMs = minRecordingMs;
        this.noSpeechMs = noSpeechMs;
        floorMin = minThreshold / factor;
    }

    public enum Decision
    {
        Continue,
        SpeechEnded,
        NoSpeech,
    }

    public long PauseMs { get; }

    /// <summary>
    /// Aktueller Rauschboden, für das Kalibrier-Log. Nie unter <c>minThreshold / factor</c>: ohne Untergrenze
    /// bliebe er nach digitalen Nullen (Mikrofon-Anlauf) für immer 0 — er steigt multiplikativ.
    /// </summary>
    public float Floor { get; private set; }

    public Decision Feed(float peak, long atMs)
    {
        if (decision != Decision.Continue)
        {
            return decision;
        }
        var dt = lastAt < 0 ? 0L : Math.Max(0L, atMs - lastAt);
        lastAt = atMs;
        // Wie Kotlins Float.pow: in double gerechnet, dann zu float.
        var risen = Floor * (float)Math.Pow(10.0, floorRiseDbPerS * dt / 20_000f);
        Floor = Math.Max(Math.Min(peak, risen), floorMin);

        var on = Math.Max(minThreshold, factor * Floor);
        var nowLoud = loud ? peak >= hysteresis * on : peak > on;
        if (nowLoud && !loud)
        {
            loudSince = atMs;
            sectionPeak = 0f;
            sectionLong = false;
        }
        if (nowLoud)
        {
            sectionPeak = Math.Max(sectionPeak, peak);
            if (atMs - loudSince >= speechConfirmMs)
            {
                sectionLong = true;
            }
        }
        if (!nowLoud && loud)
        {
            quietSince = atMs;
            // Fällt der Pegel nicht, hat nur der steigende Boden aufgeholt: Dauerlärm.
            if (sectionLong && peak < speechDrop * sectionPeak)
            {
                speech = true;
            }
        }
        loud = nowLoud;

        if (speech && !loud && atMs - quietSince >= PauseMs && atMs >= minRecordingMs)
        {
            decision = Decision.SpeechEnded;
        }
        else if (!speech && !loud && atMs >= noSpeechMs)
        {
            decision = Decision.NoSpeech;
        }
        return decision;
    }
}
