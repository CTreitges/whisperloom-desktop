using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using WhisperLoom.App.Design;
using WhisperLoom.App.Platform.Windows.Input;

namespace WhisperLoom.App.Features.Selection;

/// <summary>Lage von Wähler und Knopf am Cursor: unterhalb des Punkts, sonst darüber; immer ganz im Arbeitsbereich seines Monitors.</summary>
public static class SelectionPlacement
{
    /// <summary>Abstand zum Punkt (Schreibmarke bzw. Maus) in DIP.</summary>
    public const double GapDip = 6;

    /// <param name="workArea">Arbeitsbereich des Monitors, auf dem <paramref name="at"/> liegt (physische Pixel).</param>
    /// <param name="scaling">Skalierung dieses Monitors (Per-Monitor-DPI).</param>
    public static PixelPoint Near(PixelRect workArea, double scaling, Size windowDip, PixelPoint at)
    {
        var width = (int)Math.Ceiling(windowDip.Width * scaling);
        var height = (int)Math.Ceiling(windowDip.Height * scaling);
        var gap = (int)Math.Round(GapDip * scaling);
        var y = at.Y + gap;
        if (y + height > workArea.Bottom)
        {
            y = at.Y - gap - height;
        }
        var x = Math.Clamp(at.X, workArea.X, Math.Max(workArea.X, workArea.Right - width));
        y = Math.Clamp(y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));
        return new PixelPoint(x, y);
    }
}

/// <summary>
/// Avalonia-Fenster der Markierung (W2-SEL): Wähler und Knopf am Punkt auf dem richtigen Monitor (<see cref="Screens.ScreenFromPoint"/>,
/// Per-Monitor-DPI). Ohne Punkt sitzt der Wähler in der Mitte des Zielfensters. Nur UI-Thread.
/// </summary>
public sealed class SelectionPresenter(bool noActivate, Func<IntPtr, PixelPoint?> targetCenter) : ISelectionPresenter
{
    private SelectionPickerWindow? _picker;
    private SelectionButtonWindow? _button;

    public SelectionPickerWindow? PickerWindow => _picker;

    public SelectionButtonWindow? ButtonWindow => _button;

    public void ShowPicker(StagePickerViewModel picker, PixelPoint? at, IntPtr target)
    {
        _picker ??= new SelectionPickerWindow(noActivate);
        _picker.DataContext = picker;
        Place(_picker, at ?? targetCenter(target), new Size(260, 280));
        _picker.Show();
    }

    public void HidePicker() => _picker?.Hide();

    public void ShowButton(PixelPoint at, Action click)
    {
        _button ??= new SelectionButtonWindow();
        _button.SetClick(click);
        Place(_button, at, new Size(130, 30));
        _button.Show();
    }

    public void HideButton() => _button?.Hide();

    public IReadOnlyList<ScreenRect> Transient() =>
        [.. new Window?[] { _picker, _button }.Where(w => w?.IsVisible == true).Select(w => Bounds(w!))];

    public JsonObject Status()
    {
        var status = new JsonObject { ["picker"] = _picker?.IsVisible == true, ["button"] = null, ["options"] = null };
        if (_button?.IsVisible == true)
        {
            var b = Bounds(_button);
            status["button"] = new JsonArray((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2);
        }
        if (_picker?.IsVisible == true)
        {
            var p = Bounds(_picker);
            status["pickerRect"] = new JsonArray(p.Left, p.Top, p.Right, p.Bottom);
        }
        return status;
    }

    private static ScreenRect Bounds(Window w)
    {
        var scaling = w.Screens.ScreenFromWindow(w)?.Scaling ?? 1;
        var size = w.Bounds.Size;
        return new ScreenRect(w.Position.X, w.Position.Y, w.Position.X + (int)Math.Ceiling(size.Width * scaling),
            w.Position.Y + (int)Math.Ceiling(size.Height * scaling));
    }

    private static void Place(Window window, PixelPoint? at, Size fallback)
    {
        window.ApplyTemplate();
        window.Measure(new Size(window.Width is > 0 ? window.Width : double.PositiveInfinity, double.PositiveInfinity));
        var desired = window.DesiredSize;
        var size = desired.Width > 0 && desired.Height > 0 ? desired : fallback;
        var screen = (at is { } p ? window.Screens.ScreenFromPoint(p) : null) ?? window.Screens.Primary;
        if (screen is not null)
        {
            var point = at ?? screen.WorkingArea.Center;
            window.Position = SelectionPlacement.Near(screen.WorkingArea, screen.Scaling, size, point);
        }
    }
}
