using System.Text.Json.Nodes;
using Avalonia;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.Rewrite;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>UI Automation TextPattern als Fake: null = kein Element, sonst die Auskunft; zählt die Abfragen.</summary>
internal sealed class FakeSelectionSource : ISelectionTextSource
{
    public UiaSelection? Next { get; set; }

    public int Calls { get; private set; }

    public UiaSelection? Selection()
    {
        Calls++;
        return Next;
    }
}

/// <summary>Wähler und Knopf ohne Fenster: merkt, was gezeigt wurde.</summary>
internal sealed class FakeSelectionPresenter : ISelectionPresenter
{
    public StagePickerViewModel? Picker { get; private set; }

    public PixelPoint? PickerAt { get; private set; }

    public PixelPoint? ButtonAt { get; private set; }

    public Action? Click { get; private set; }

    public int PickerShows { get; private set; }

    public void ShowPicker(StagePickerViewModel picker, PixelPoint? at, IntPtr target)
    {
        Picker = picker;
        PickerAt = at;
        PickerShows++;
    }

    public void HidePicker() => Picker = null;

    public void ShowButton(PixelPoint at, Action click)
    {
        ButtonAt = at;
        Click = click;
    }

    public void HideButton()
    {
        ButtonAt = null;
        Click = null;
    }

    public IReadOnlyList<ScreenRect> Transient() =>
        [.. new[] { Picker is null ? (ScreenRect?)null : new ScreenRect(0, 0, 100, 100), ButtonAt is { } b ? new ScreenRect(b.X, b.Y, b.X + 50, b.Y + 20) : null }
            .OfType<ScreenRect>()];

    public JsonObject Status() => new() { ["picker"] = Picker is not null, ["button"] = ButtonAt is null ? null : new JsonArray(ButtonAt.Value.X, ButtonAt.Value.Y) };
}

/// <summary>Bausteine für Ablauf-Tests der Markierung (W2-SEL).</summary>
internal static class SelectionTesting
{
    /// <summary>Markierungs-Teile auf dem <see cref="InsertRig"/>: UIA-Fake (ab Werk ohne TextPattern → Strg+C), Fake-Fenster.</summary>
    public static SelectionWiring Wiring(
        InsertRig rig,
        FakeSelectionSource? uia = null,
        FakeSelectionPresenter? ui = null,
        IKeySender? keys = null,
        Action<string>? log = null) => new()
    {
        Reader = new SelectionReader(rig.Focus, uia ?? new FakeSelectionSource(),
            new SelectionCapture(rig.Clipboard, keys ?? rig.Keys, rig.Windows, TimeSpan.Zero), rig.Windows, TimeSpan.FromSeconds(5)),
        Ui = ui ?? new FakeSelectionPresenter(),
        PickerKeys = on => log?.Invoke(on ? "picker-keys-on" : "picker-keys-off"),
        DismissOnKey = on => log?.Invoke(on ? "dismiss-on" : "dismiss-off"),
        ReplayRightClick = () =>
        {
            log?.Invoke("replay-right-click");
            return true;
        },
        Anchor = _ => new PixelPoint(300, 200),
    };

    /// <summary>Kombi → Wähler → „Nach Anweisung umschreiben …“ (früher: Kombi „Umschreiben“).</summary>
    public static async Task StartInstructionAsync(InputFlow flow)
    {
        await flow.HandleAsync(HotkeyAction.Selection);
        if (flow.Picker is not null)
        {
            await flow.ChooseSelectionAsync(InputFlow.InstructionKey);
        }
    }
}
