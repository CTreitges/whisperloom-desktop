using System.Text.Json;

namespace WhisperLoom.TestTarget;

/// <summary>
/// Testziel für Einfügen und Tippen: ein Fenster "WhisperLoom TestTarget" mit Textfeld. Jede Änderung schreibt
/// <c>{"text", "enter", "backspace", "hwnd", "ready", "focused", "box"}</c> (box = Textfeld in Bildschirm-Pixeln [l, t, r, b], W2-SEL) nach <c>--out &lt;datei&gt;</c>. Enter (ohne Umschalt) fügt keinen
/// Umbruch ein, sondern zählt nur — wie ein Chat, der abschickt. Beendet sich nach <c>--seconds</c> (Standard 120) selbst.
/// <c>--text &lt;starttext&gt;</c> steht beim Start im Feld und ist ganz markiert (Umschreiben der Markierung, WLD-P5b).
/// <c>--mode password</c>: das Feld ist ein Passwortfeld; <c>--mode button</c>: ein Knopf hat den Fokus, kein Textfeld (W2-OUT).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = Arg(args, "--out");
        if (outPath is null)
        {
            Console.Error.WriteLine("Aufruf: WhisperLoom.TestTarget --out <datei> [--seconds <n>] [--text <starttext>]");
            return 2;
        }
        var seconds = int.TryParse(Arg(args, "--seconds"), out var s) ? s : 120;
        ApplicationConfiguration.Initialize();
        Application.Run(new TargetForm(outPath, TimeSpan.FromSeconds(seconds), Arg(args, "--text") ?? "", Arg(args, "--mode") ?? "text"));
        return 0;
    }

    private static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

internal sealed class TargetForm : Form
{
    private readonly string _outPath;
    private readonly TextBox _box;
    private readonly Button _button = new() { Text = "OK", Dock = DockStyle.Fill };
    private readonly string _mode;
    private int _enter;
    private int _backspace;
    private bool _ready;

    public TargetForm(string outPath, TimeSpan lifetime, string initialText = "", string mode = "text")
    {
        _outPath = outPath;
        _mode = mode;
        // Passwortfeld: einzeilig mit Ersatzzeichen (ES_PASSWORD, UIA IsPassword). Knopf-Modus: Knopf füllt das Fenster
        // (ein Klick in die Mitte des Smokes trifft den Knopf), das Feld bleibt oben einzeilig.
        _box = mode != "text"
            ? new TextBox { UseSystemPasswordChar = mode == "password", Dock = DockStyle.Top, Font = new Font("Segoe UI", 14) }
            : new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14) };
        Text = "WhisperLoom TestTarget";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 320);
        TopMost = true;
        Controls.Add(_box);
        if (mode == "button")
        {
            Controls.Add(_button);
            _button.GotFocus += (_, _) => Save();
        }
        _box.KeyDown += OnKeyDown;
        _box.TextChanged += (_, _) => Save();
        _box.GotFocus += (_, _) => Save();
        _box.LostFocus += (_, _) => Save();
        Activated += (_, _) => Save();
        Deactivate += (_, _) => Save();
        // Bereit erst, wenn das Fenster sichtbar ist und das Textfeld den Fokus hat: vorher meldet die Datei kein hwnd,
        // damit der Smoke nicht zu früh Tasten drückt (Fall hold-insert beim kalten ersten Start).
        Shown += (_, _) =>
        {
            _box.Text = initialText;
            _box.SelectAll();
            if (_mode == "button")
            {
                _button.Focus();
            }
            else
            {
                _box.Focus();
            }
            _ready = true;
            Save();
        };
        var timer = new System.Windows.Forms.Timer { Interval = (int)lifetime.TotalMilliseconds };
        timer.Tick += (_, _) => Close();
        timer.Start();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.A && e.Control && !e.Alt)
        {
            // Mehrzeilige WinForms-Felder kennen Strg+A nicht von selbst (Smoke rewrite-selection markiert damit).
            _box.SelectAll();
            e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            _enter++;
            e.SuppressKeyPress = true;
            Save();
        }
        else if (e.KeyCode == Keys.Enter && e.Shift)
        {
            // Umschalt+Enter: Zeilenumbruch (direkt tippen schickt Umbrüche so).
            _box.SelectedText = "\n";
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Back)
        {
            _backspace++;
            Save();
        }
    }

    private void Save()
    {
        if (!_ready)
        {
            return;
        }
        var focused = (_mode == "button" ? _button.Focused : _box.Focused) && ContainsFocus;
        var r = _box.RectangleToScreen(_box.ClientRectangle);
        var box = new[] { r.Left, r.Top, r.Right, r.Bottom };
        var json = JsonSerializer.Serialize(new { text = _box.Text, enter = _enter, backspace = _backspace, hwnd = Handle.ToInt64(), ready = true, focused, box });
        var tmp = _outPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _outPath, overwrite: true);
    }
}
