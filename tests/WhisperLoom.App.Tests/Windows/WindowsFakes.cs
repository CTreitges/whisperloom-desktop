using System.Text.Json.Nodes;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.TextOutput;

namespace WhisperLoom.App.Tests.Windows;

/// <summary>Gemeinsames Protokoll der Fakes: Reihenfolge von Zwischenablage- und Tastatur-Schritten.</summary>
internal sealed class Steps : List<string>;

internal sealed class FakeClipboard(Steps steps) : IClipboardAdapter
{
    public string? Text { get; set; } = "alt";

    public bool Private { get; private set; }

    public uint SequenceNumber { get; set; } = 1;

    /// <summary>Simuliert ein anderes Programm, das während des Einfügens kopiert.</summary>
    public bool ChangedByOthers { get; set; }

    public ClipboardSnapshot Save()
    {
        steps.Add("save");
        return new ClipboardSnapshot(Text is null ? [] : [(13u, System.Text.Encoding.Unicode.GetBytes(Text))]);
    }

    public void SetText(string text, bool @private)
    {
        steps.Add(@private ? "set-private" : "set");
        Text = text;
        Private = @private;
        SequenceNumber++;
    }

    public void Restore(ClipboardSnapshot snapshot)
    {
        steps.Add("restore");
        Text = snapshot.Items.Count == 0 ? null : System.Text.Encoding.Unicode.GetString(snapshot.Items[0].Data);
        Private = true;
        SequenceNumber++;
    }

    public void OnPaste()
    {
        if (ChangedByOthers)
        {
            Text = "fremd";
            SequenceNumber++;
        }
    }
}

internal sealed class FakeKeys(Steps steps, FakeClipboard clipboard) : IKeySender
{
    public bool Reject { get; set; }

    public string Target { get; private set; } = "";

    public int Enters { get; private set; }

    public Task WaitForModifiersReleasedAsync(TimeSpan max, CancellationToken ct)
    {
        steps.Add("wait-modifiers");
        return Task.CompletedTask;
    }

    public bool SendCtrlV()
    {
        steps.Add("ctrl-v");
        if (Reject)
        {
            return false;
        }
        Target += clipboard.Text;
        clipboard.OnPaste();
        return true;
    }

    public bool SendEnter()
    {
        steps.Add("enter");
        Enters++;
        return true;
    }

    public bool Type(string text)
    {
        steps.Add("type");
        if (Reject)
        {
            return false;
        }
        Target += text;
        return true;
    }
}

internal sealed class FakeWindows(Steps steps) : IWindowAdapter
{
    public IntPtr Current { get; set; } = 42;

    public HashSet<IntPtr> Alive { get; } = [42];

    public HashSet<IntPtr> Elevated { get; } = [];

    public bool RefuseActivate { get; set; }

    public IntPtr Foreground() => Current;

    public bool Exists(IntPtr window) => Alive.Contains(window);

    public bool IsProtected(IntPtr window) => Elevated.Contains(window);

    public bool Activate(IntPtr window)
    {
        steps.Add("activate");
        if (!RefuseActivate)
        {
            Current = window;
        }
        return Current == window;
    }
}

/// <summary>Alles für einen Einfüge-Ablauf ohne echte Eingabe.</summary>
internal sealed class InsertRig
{
    public InsertRig()
    {
        Clipboard = new FakeClipboard(Steps);
        Keys = new FakeKeys(Steps, Clipboard);
        Windows = new FakeWindows(Steps);
        Inserter = new TextInserter(Clipboard, Keys, Windows, TimeSpan.Zero);
    }

    public Steps Steps { get; } = [];

    public FakeClipboard Clipboard { get; }

    public FakeKeys Keys { get; }

    public FakeWindows Windows { get; }

    public TextInserter Inserter { get; }
}

internal sealed class FakePresenter : IInputPresenter
{
    public bool Overlay { get; private set; }

    public bool Pill { get; private set; }

    public bool PillProcessing { get; private set; }

    public List<(string Kind, string Caption, string Message)> Hints { get; } = [];

    public IntPtr LastTarget { get; private set; }

    public void ShowOverlay(OverlayViewModel overlay, IntPtr target)
    {
        Overlay = true;
        LastTarget = target;
    }

    public void HideOverlay() => Overlay = false;

    public void ShowPill(IntPtr target)
    {
        Pill = true;
        PillProcessing = false;
        LastTarget = target;
    }

    public void SetPill(double level, bool processing) => PillProcessing = processing;

    public void HidePill() => Pill = false;

    public void ShowHint(string kind, string caption, string message, TimeSpan duration, IntPtr target) =>
        Hints.Add((kind, caption, message));

    public JsonObject Status() => new() { ["overlay"] = Overlay, ["pill"] = Pill };
}
