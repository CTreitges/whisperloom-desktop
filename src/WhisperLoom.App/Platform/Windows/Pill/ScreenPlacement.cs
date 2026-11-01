using Avalonia;

namespace WhisperLoom.App.Platform.Windows.Pill;

/// <summary>
/// Lage der fokusfreien Fenster: unten mittig im Arbeitsbereich des Monitors, auf dem das Zielfenster liegt.
/// Arbeitsbereich in physischen Pixeln, Fenstergröße in DIP; <paramref name="scaling"/> ist die Skalierung dieses
/// Monitors (Per-Monitor-DPI, z. B. 1,5 bei 150 %).
/// </summary>
public static class ScreenPlacement
{
    public const double BottomMarginDip = 72;

    public static PixelPoint BottomCenter(PixelRect workArea, double scaling, Size windowDip, double marginDip = BottomMarginDip)
    {
        var width = (int)Math.Ceiling(windowDip.Width * scaling);
        var height = (int)Math.Ceiling(windowDip.Height * scaling);
        var x = workArea.X + (workArea.Width - width) / 2;
        var y = workArea.Bottom - height - (int)Math.Round(marginDip * scaling);
        return new PixelPoint(x, Math.Max(workArea.Y, y));
    }

    /// <summary>Mittelpunkt eines Fensterrechtecks (physische Pixel); dient zur Wahl des Monitors.</summary>
    public static PixelPoint Center(int left, int top, int right, int bottom) => new((left + right) / 2, (top + bottom) / 2);
}
