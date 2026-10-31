using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Fenster-Styles der fokusfreien Fenster (Spike P3) und ihre Lage auf dem Monitor des Ziels.</summary>
public class OverlayStylesTest
{
    private const uint WsExAppWindow = 0x00040000;
    private const uint WsExLayered = 0x00080000;

    [Fact]
    public void Ex_style_adds_noactivate_toolwindow_topmost_and_drops_appwindow()
    {
        var style = NoActivate.ExStyle(WsExAppWindow | WsExLayered);
        Assert.Equal(0x08000000u, style & 0x08000000u);
        Assert.Equal(0x00000080u, style & 0x00000080u);
        Assert.Equal(0x00000008u, style & 0x00000008u);
        Assert.Equal(0u, style & WsExAppWindow);
        Assert.Equal(WsExLayered, style & WsExLayered);
    }

    [Fact]
    public void Mouse_activate_is_answered_with_ma_noactivate()
    {
        var handled = false;
        Assert.Equal((IntPtr)3, NoActivate.WndProc(1, 0x0021, 0, 0, ref handled));
        Assert.True(handled);

        handled = false;
        Assert.Equal(IntPtr.Zero, NoActivate.WndProc(1, 0x0201, 0, 0, ref handled));
        Assert.False(handled);
    }

    [AvaloniaFact]
    public void Overlay_pill_and_hint_never_activate()
    {
        Window[] windows = [new OverlayWindow(new OverlayViewModel(() => { }, () => { }, _ => { })), new PillWindow(new PillViewModel()), new HintWindow()];
        foreach (var w in windows)
        {
            Assert.False(w.ShowActivated);
            Assert.True(w.Topmost);
            Assert.False(w.ShowInTaskbar);
            Assert.Equal(WindowDecorations.None, w.WindowDecorations);
        }
    }

    [Fact]
    public void Placement_bottom_center_at_100_percent()
    {
        var p = ScreenPlacement.BottomCenter(new PixelRect(0, 0, 1920, 1040), 1.0, new Size(200, 44));
        Assert.Equal(new PixelPoint(860, 1040 - 44 - 72), p);
    }

    [Fact]
    public void Placement_on_second_monitor_at_150_percent_uses_physical_pixels()
    {
        // Rechter Monitor 2560×1440 bei 150 %, Taskleiste 72 px.
        var p = ScreenPlacement.BottomCenter(new PixelRect(1920, 0, 2560, 1368), 1.5, new Size(200, 44));
        Assert.Equal(1920 + (2560 - 300) / 2, p.X);
        Assert.Equal(1368 - 66 - 108, p.Y);
    }

    [Fact]
    public void Placement_never_leaves_the_top_of_the_work_area()
    {
        var p = ScreenPlacement.BottomCenter(new PixelRect(0, -1080, 1920, 100), 1.0, new Size(200, 200));
        Assert.Equal(-1080, p.Y);
    }

    [Fact]
    public void Center_of_window_rect()
    {
        Assert.Equal(new PixelPoint(2000, 500), ScreenPlacement.Center(1900, 400, 2100, 600));
    }
}
