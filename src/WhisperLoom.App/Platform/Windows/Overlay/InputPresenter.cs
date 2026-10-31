using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Pill;

namespace WhisperLoom.App.Platform.Windows.Overlay;

/// <summary>
/// Avalonia-Fenster des <see cref="InputFlow"/>: Overlay, Pegel-Pille und Hinweis, alle fokusfrei und unten mittig auf
/// dem Monitor des Zielfensters (Per-Monitor-DPI über <see cref="Screen.Scaling"/>). Nur UI-Thread.
/// </summary>
public sealed class InputPresenter : IInputPresenter
{
    private readonly PillViewModel _pill = new();
    private readonly DispatcherTimer _hintTimer = new();
    private readonly Func<IntPtr, PixelPoint?> _targetCenter;
    private OverlayWindow? _overlay;
    private PillWindow? _pillWindow;
    private HintWindow? _hint;

    /// <param name="targetCenter">Mittelpunkt des Zielfensters in Bildschirm-Pixeln (null = unbekannt → Hauptmonitor).</param>
    public InputPresenter(Func<IntPtr, PixelPoint?> targetCenter)
    {
        _targetCenter = targetCenter;
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            _hint?.Hide();
        };
    }

    public void ShowOverlay(OverlayViewModel overlay, IntPtr target)
    {
        _overlay ??= new OverlayWindow(overlay);
        Show(_overlay, target, new Size(560, 130));
    }

    public void HideOverlay() => _overlay?.Hide();

    public void ShowPill(IntPtr target)
    {
        _pill.Level = 0;
        _pill.Processing = false;
        _pillWindow ??= new PillWindow(_pill);
        Show(_pillWindow, target, new Size(200, 44));
    }

    public void SetPill(double level, bool processing)
    {
        _pill.Level = level;
        _pill.Processing = processing;
    }

    public void HidePill() => _pillWindow?.Hide();

    public void ShowHint(string kind, string caption, string message, TimeSpan duration, IntPtr target)
    {
        _hint ??= new HintWindow();
        _hint.SetContent(kind, caption, message);
        // Der Hinweis sitzt über Overlay bzw. Pille.
        var lift = _overlay?.IsVisible == true || _pillWindow?.IsVisible == true ? 150 : 0;
        Show(_hint, target, new Size(360, 64), ScreenPlacement.BottomMarginDip + lift);
        _hintTimer.Stop();
        _hintTimer.Interval = duration;
        _hintTimer.Start();
    }

    public JsonObject Status() => new()
    {
        ["overlay"] = _overlay?.IsVisible == true,
        ["buttons"] = _overlay?.IsVisible == true ? _overlay.ButtonCenters() : null,
        ["pill"] = _pillWindow?.IsVisible == true,
        ["hint"] = _hint?.IsVisible == true ? new JsonObject { ["kind"] = _hint.Kind, ["text"] = _hint.Text } : null,
    };

    private void Show(Window window, IntPtr target, Size fallback, double margin = ScreenPlacement.BottomMarginDip)
    {
        var size = Measure(window, fallback);
        var screen = (_targetCenter(target) is { } center ? window.Screens.ScreenFromPoint(center) : null) ?? window.Screens.Primary;
        if (screen is not null)
        {
            window.Position = ScreenPlacement.BottomCenter(screen.WorkingArea, screen.Scaling, size, margin);
        }
        window.Show();
    }

    private static Size Measure(Window window, Size fallback)
    {
        if (window.SizeToContent == SizeToContent.Manual)
        {
            return new Size(window.Width, window.Height);
        }
        window.ApplyTemplate();
        window.Measure(new Size(window.MaxWidth, double.PositiveInfinity));
        var desired = window.DesiredSize;
        var width = window.SizeToContent.HasFlag(SizeToContent.Width) ? desired.Width : window.Width;
        var height = desired.Height;
        return width > 0 && height > 0 ? new Size(width, height) : fallback;
    }
}
