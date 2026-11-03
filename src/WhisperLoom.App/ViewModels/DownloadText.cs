using WhisperLoom.App.Localization;

namespace WhisperLoom.App.ViewModels;

/// <summary>
/// Fortschritt eines Modell-Downloads als Satz (zielstruktur.md §7, B30): „540 von 1117 MB · noch etwa 2 Min.“; ohne Rate
/// (noch keine Messung) nur die Megabyte, unter einer Minute „noch weniger als 1 Min.“.
/// </summary>
public static class DownloadText
{
    public static string Of(long bytes, long total, long bytesPerSec)
    {
        var loc = Loc.Instance;
        var done = loc.Format("Download_Progress", bytes / 1_000_000, Math.Max(total, bytes) / 1_000_000);
        if (bytesPerSec <= 0 || total <= bytes)
        {
            return done;
        }
        var seconds = (total - bytes) / (double)bytesPerSec;
        return seconds < 60 ? loc.Format("Download_EtaUnderMinute", done) : loc.Format("Download_Eta", done, (int)Math.Ceiling(seconds / 60));
    }

    /// <summary>
    /// Fortschritt ohne Bytes (Velopack meldet nur Prozent, UX-6 N19): „42 % · noch etwa 2 Min.“; die Restzeit schätzt die
    /// bisherige Dauer hoch, erst ab 5 % und 2 s (vorher schwankt sie zu stark).
    /// </summary>
    public static string OfPercent(int percent, TimeSpan elapsed)
    {
        var loc = Loc.Instance;
        percent = Math.Clamp(percent, 0, 100);
        var done = loc.Format("Download_Percent", percent);
        if (percent < 5 || percent >= 100 || elapsed < TimeSpan.FromSeconds(2))
        {
            return done;
        }
        var seconds = elapsed.TotalSeconds * (100 - percent) / percent;
        return seconds < 60 ? loc.Format("Download_EtaUnderMinute", done) : loc.Format("Download_Eta", done, (int)Math.Ceiling(seconds / 60));
    }
}
