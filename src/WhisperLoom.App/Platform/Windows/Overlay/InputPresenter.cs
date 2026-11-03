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
    private readonly Design.ISystemPreferences _preferences = Design.SystemPreferences.ForCurrentOs();
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
        HideStickyHint();
        overlay.Motion = _preferences.AnimationsEnabled;
        _overlay ??= new OverlayWindow(overlay);
        Show(_overlay, target, new Size(560, 130));
    }

    public void HideOverlay() => _overlay?.Hide();

    public void ShowPill(IntPtr target)
    {
        HideStickyHint();
        _pill.Level = 0;
        _pill.Processing = false;
        _pill.Motion = _preferences.AnimationsEnabled;
        _pillWindow ??= new PillWindow(_pill);
        Show(_pillWindow, target, new Size(200, 56));
    }

    public void SetPill(double level, bool processing)
    {
        _pill.Level = level;
        _pill.Processing = processing;
    }

    public void HidePill() => _pillWindow?.Hide();

    public void ShowHint(string kind, string caption, string message, TimeSpan duration, IntPtr target, HintTone tone = HintTone.Info,
        IReadOnlyList<string>? keys = null)
    {
        _hint ??= new HintWindow();
        _hint.SetContent(kind, caption, message, tone, keys);
        // Warnung/Fehler tragen Wesentliches (nicht eingefügt, Grenze, Fehler): kein Ablauf per Zeit (Richtlinie 04 §4.9, WCAG 2.2.1).
        _hint.Sticky = Sticks(tone);
        // Der Hinweis sitzt über Overlay bzw. Pille.
        var lift = _overlay?.IsVisible == true || _pillWindow?.IsVisible == true ? 150 : 0;
        Show(_hint, target, new Size(360, 64), ScreenPlacement.BottomMarginDip + lift);
        _hintTimer.Stop();
        if (!_hint.Sticky)
        {
            _hintTimer.Interval = duration;
            _hintTimer.Start();
        }
    }

    /// <summary>Welche Hinweise stehen bleiben: Warnung und Fehler; Stufe, Kopiert und Auskunft verschwinden von selbst.</summary>
    internal static bool Sticks(HintTone tone) => tone is HintTone.Warning or HintTone.Error;

    /// <summary>Läuft die Zeit des Hinweises (Tests: stehende Hinweise starten sie nicht).</summary>
    internal bool HintTimerRunning => _hintTimer.IsEnabled;

    /// <summary>Ein neues Diktat nimmt einen stehenden Hinweis weg; er gehört zum vorigen.</summary>
    private void HideStickyHint()
    {
        if (_hint is { Sticky: true, IsVisible: true })
        {
            _hint.Hide();
        }
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
