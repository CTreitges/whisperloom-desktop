using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Audio;

/// <summary>
/// Schneidet lange Stille aus einer Aufnahme, bevor sie zur Erkennung geht: abgerechnet wird die gesendete Dauer, und
/// lange Pausen kosten sonst Geld ohne Text (Christof 05.11.: Schwelle 5 s). Kein Android-Vorbild, nur Desktop.
/// <list type="bullet">
/// <item>Gemessen wird je Fenster von <see cref="WindowMs"/> ms der Spitzenpegel (Sample / 32768). Leise ist ein
/// Fenster unter <see cref="Threshold"/> — der Pegel, unter dem der <see cref="AutoStopDetector"/> einen lauten
/// Abschnitt beendet (<c>Hysteresis * MinThreshold</c>).</item>
/// <item>Eine Folge leiser Fenster ab <see cref="MinPauseMs"/> wird entfernt; zur Sprache hin bleiben je
/// <see cref="KeepMs"/> ms stehen, damit keine leisen Silbenränder verloren gehen und die Erkennung die Pause noch als
/// Satzgrenze sieht. Am Anfang und Ende bleibt nur der Rand zur Sprache.</item>
/// <item>Alle übrigen Samples (Sprache, kürzere Pausen) bleiben bit-gleich und in ihrer Reihenfolge.</item>
/// <item>Komplett stille Aufnahme ab <see cref="MinPauseMs"/>: leerer Puffer — die Pipeline sendet dann nichts.</item>
/// </list>
/// </summary>
public static class PauseCut
{
    /// <summary>Ab dieser Stille wird geschnitten (fest, keine Einstellung).</summary>
    public const long MinPauseMs = 5_000L;

    /// <summary>So viel Stille bleibt je Seite zur Sprache hin stehen.</summary>
    public const long KeepMs = 150L;

    public const int WindowMs = 10;

    /// <summary>Spitzenpegel (0..1), unter dem ein Fenster als leise gilt.</summary>
    public const float Threshold = AutoStopDetector.Hysteresis * AutoStopDetector.MinThreshold;

    /// <summary>Aufnahme ohne lange Pausen; ohne solche Pause derselbe Puffer.</summary>
    public static AudioBuffer Apply(AudioBuffer audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        var samples = audio.Samples.Span;
        var cuts = Cuts(samples, audio.SampleRate);
        if (cuts.Count == 0) return audio;

        var removed = cuts.Sum(c => c.End - c.Start);
        var kept = new short[samples.Length - removed];
        var from = 0;
        var to = 0;
        foreach (var (start, end) in cuts)
        {
            samples[from..start].CopyTo(kept.AsSpan(to));
            to += start - from;
            from = end;
        }
        samples[from..].CopyTo(kept.AsSpan(to));
        return new AudioBuffer(kept, audio.SampleRate);
    }

    /// <summary>Zu entfernende Bereiche [Start, End) in Samples, aufsteigend und überlappungsfrei.</summary>
    private static List<(int Start, int End)> Cuts(ReadOnlySpan<short> samples, int sampleRate)
    {
        var window = Math.Max(1, sampleRate * WindowMs / 1000);
        var minPause = (int)(sampleRate * MinPauseMs / 1000);
        var keep = (int)(sampleRate * KeepMs / 1000);
        var cuts = new List<(int, int)>();
        var quietStart = -1;
        for (var pos = 0; pos < samples.Length; pos += window)
        {
            var end = Math.Min(pos + window, samples.Length);
            if (IsQuiet(samples[pos..end]))
            {
                if (quietStart < 0) quietStart = pos;
            }
            else
            {
                AddCut(cuts, quietStart, pos, samples.Length, minPause, keep);
                quietStart = -1;
            }
        }
        AddCut(cuts, quietStart, samples.Length, samples.Length, minPause, keep);
        return cuts;
    }

    private static void AddCut(List<(int, int)> cuts, int quietStart, int quietEnd, int length, int minPause, int keep)
    {
        if (quietStart < 0 || quietEnd - quietStart < minPause) return;
        // Am Rand der Aufnahme gibt es keine Sprache, deren Silben zu schützen wären.
        var start = quietStart == 0 ? 0 : quietStart + keep;
        var end = quietEnd == length ? length : quietEnd - keep;
        cuts.Add((start, end));
    }

    private static bool IsQuiet(ReadOnlySpan<short> window)
    {
        foreach (var s in window)
        {
            if (Math.Abs(s / 32768f) >= Threshold) return false;
        }
        return true;
    }
}
