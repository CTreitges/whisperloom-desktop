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

    /// <summary>Privates Setzen scheitert nach dem Leeren (Zwischenablage belegt).</summary>
    public bool FailPrivateSet { get; set; }

    public ClipboardSnapshot Save()
    {
        steps.Add("save");
        return new ClipboardSnapshot(Text is null ? [] : [(13u, System.Text.Encoding.Unicode.GetBytes(Text))]);
    }

    public void SetText(string text, bool @private)
    {
        steps.Add(@private ? "set-private" : "set");
        if (@private && FailPrivateSet)
        {
            Text = null;
            throw new IOException("belegt");
        }
        Text = text;
        Private = @private;
        SequenceNumber++;
    }

    /// <summary>So oft scheitert Restore noch (Zwischenablage belegt).</summary>
    public int FailRestores { get; set; }

    public void Restore(ClipboardSnapshot snapshot)
    {
        steps.Add("restore");
        if (FailRestores > 0)
        {
            FailRestores--;
            throw new IOException("belegt");
        }
        Text = snapshot.Items.Count == 0 ? null : System.Text.Encoding.Unicode.GetString(snapshot.Items[0].Data);
        Private = true;
        SequenceNumber++;
    }

    public string? GetText()
    {
        steps.Add("get-text");
        return Text;
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

    /// <summary>Läuft nach Strg+V (z. B. der Nutzer wechselt das Fenster).</summary>
    public Action? AfterPaste { get; set; }

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
        AfterPaste?.Invoke();
        return true;
    }

    /// <summary>Was das Ziel markiert hat; null = nichts (Strg+C ändert die Zwischenablage nicht).</summary>
    public string? Selection { get; set; }

    public bool SendCopy(bool ctrlInsert)
    {
        steps.Add(ctrlInsert ? "ctrl-insert" : "ctrl-c");
        if (Selection is not null)
        {
            clipboard.Text = Selection;
            clipboard.SequenceNumber++;
        }
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
        AfterPaste?.Invoke();
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

    /// <summary>Prozessnamen je Fenster; sonst „proc-&lt;handle&gt;“.</summary>
    public Dictionary<IntPtr, string> Processes { get; } = [];

    public string ProcessName(IntPtr window) => Processes.GetValueOrDefault(window, $"proc-{(long)window}");

    public string FocusClass(IntPtr window) => window == 42 ? "Edit" : "";

    /// <summary>Fensterklassen je Fenster (Electron: Chrome_WidgetWin_1); sonst "".</summary>
    public Dictionary<IntPtr, string> Classes { get; } = [];

    public string WindowClass(IntPtr window) => Classes.GetValueOrDefault(window, "");

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

/// <summary>Fokus-Klasse nach dem Aktivieren (statt UI Automation); zählt die Abfragen.</summary>
internal sealed class FakeFocus : IFocusProbe
{
    public WhisperLoom.Core.Output.FocusKind Kind { get; set; } = WhisperLoom.Core.Output.FocusKind.Editable;

    public int Probes { get; private set; }

    /// <summary>Fokussiertes Element (UIA-RuntimeId); null = unbekannt.</summary>
    public string? Element { get; set; } = "el-1";

    /// <summary>Läuft bei jeder Fokus-Abfrage (z. B. der Nutzer wechselt währenddessen Fenster oder Element).</summary>
    public Action? OnProbe { get; set; }

    public Task<WhisperLoom.Core.Output.FocusKind> ProbeAsync(CancellationToken ct)
    {
        Probes++;
        OnProbe?.Invoke();
        return Task.FromResult(Kind);
    }

    public Task<string?> FocusedElementAsync(CancellationToken ct) => Task.FromResult(Element);
}

/// <summary>Alles für einen Einfüge-Ablauf ohne echte Eingabe. Fokus ab Werk: Eingabefeld.</summary>
internal sealed class InsertRig
{
    public InsertRig()
    {
        Clipboard = new FakeClipboard(Steps);
        Keys = new FakeKeys(Steps, Clipboard);
        Windows = new FakeWindows(Steps);
        Inserter = new TextInserter(Clipboard, Keys, Windows, TimeSpan.Zero, Focus, _ => Task.CompletedTask);
    }

    public FakeFocus Focus { get; } = new();

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

/// <summary>Aktiviert scheinbar das Ziel, meldet danach aber ein anderes Vordergrundfenster.</summary>
internal sealed class SwitchingWindows(FakeWindows inner, IntPtr other) : IWindowAdapter
{
    private bool _activated;

    public IntPtr Foreground() => _activated ? other : inner.Foreground();

    public bool Exists(IntPtr window) => inner.Exists(window);

    public bool IsProtected(IntPtr window) => inner.IsProtected(window);

    public bool Activate(IntPtr window)
    {
        _activated = true;
        return true;
    }

    public string ProcessName(IntPtr window) => inner.ProcessName(window);

    public string FocusClass(IntPtr window) => inner.FocusClass(window);
}
