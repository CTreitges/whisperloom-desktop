using System.Text.Json.Nodes;
using Avalonia;
using WhisperLoom.App.Design;
using WhisperLoom.App.Platform.Windows.Input;

namespace WhisperLoom.App.Features.Selection;

/// <summary>Womit „Markierung verarbeiten“ gestartet wurde (Log, Verhalten ohne Markierung).</summary>
public enum SelectionTrigger
{
    Hotkey,
    Button,
    CtrlRightClick,
    Tray,
    Cli,
}

/// <summary>Fokusfreie Fenster der Markierung (W2-SEL): Stufen-Wähler am Cursor und schwebender Knopf. Avalonia bzw. Fake.</summary>
public interface ISelectionPresenter
{
    /// <param name="at">Bildschirm-Pixel (Schreibmarke bzw. Maus); null = Mitte des Zielfensters.</param>
    void ShowPicker(StagePickerViewModel picker, PixelPoint? at, IntPtr target);

    void HidePicker();

    /// <param name="shortcut">Tastenkürzel „Markierung verarbeiten“ für die Tastenkappe am Knopf; null = keine.</param>
    void ShowButton(PixelPoint at, Action click, string? shortcut = null);

    void HideButton();

    /// <summary>Sichtbare Fenster in Bildschirm-Pixeln (Klick daneben schließt sie, Maus-Hook).</summary>
    IReadOnlyList<ScreenRect> Transient();

    /// <summary>Für <c>status --json</c> (Smoke): Wähler (Stufen, markierte), Knopf (Mitte).</summary>
    JsonObject Status();
}

/// <summary>Was der Markierungs-Ablauf vom Betriebssystem braucht; Windows setzt es in <c>WindowsInput</c> zusammen, Tests mit Fakes.</summary>
public sealed class SelectionWiring
{
    public required SelectionReader Reader { get; init; }

    public required ISelectionPresenter Ui { get; init; }

    /// <summary>Tastatur-Hook: Wähler-Tasten an den Wähler (true) bzw. wieder frei.</summary>
    public Action<bool> PickerKeys { get; init; } = _ => { };

    /// <summary>Tastatur-Hook: jede Taste schließt den Knopf (true) bzw. nicht mehr.</summary>
    public Action<bool> DismissOnKey { get; init; } = _ => { };

    /// <summary>Maus-Hook: sichtbare Fenster für „Klick daneben schließt“.</summary>
    public Action<IReadOnlyList<ScreenRect>> TransientChanged { get; init; } = _ => { };

    /// <summary>Schreibmarke des Zielfensters, sonst Mausposition (Bildschirm-Pixel).</summary>
    public Func<IntPtr, PixelPoint?> Anchor { get; init; } = _ => null;

    /// <summary>Strg+Rechtsklick ohne Markierung: den Rechtsklick nachspielen (normales Kontextmenü).</summary>
    public Func<bool> ReplayRightClick { get; init; } = () => false;

    /// <summary>Hauptfenster unter einem Bildschirmpunkt (Strg+Rechtsklick); <see cref="IntPtr.Zero"/> = unbekannt.</summary>
    public Func<PixelPoint, IntPtr> WindowAt { get; init; } = _ => IntPtr.Zero;

    /// <summary>Tray: Fenster nach vorn holen (das Tray-Menü hatte den Fokus).</summary>
    public Func<IntPtr, bool> Activate { get; init; } = _ => true;

    /// <summary>Tray: letztes fremdes Vordergrundfenster vor dem Klick ins Tray.</summary>
    public Func<IntPtr> LastExternal { get; init; } = () => IntPtr.Zero;

    public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
}
