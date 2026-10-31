using System.Text.Json;

namespace WhisperLoom.TestTarget;

/// <summary>
/// Testziel für Einfügen und Tippen: ein Fenster "WhisperLoom TestTarget" mit Textfeld. Jede Änderung schreibt
/// <c>{"text", "enter", "backspace", "focused"}</c> nach <c>--out &lt;datei&gt;</c>. Enter (ohne Umschalt) fügt keinen
/// Umbruch ein, sondern zählt nur — wie ein Chat, der abschickt. Beendet sich nach <c>--seconds</c> (Standard 120) selbst.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var outPath = Arg(args, "--out");
        if (outPath is null)
        {
            Console.Error.WriteLine("Aufruf: WhisperLoom.TestTarget --out <datei> [--seconds <n>]");
            return 2;
        }
        var seconds = int.TryParse(Arg(args, "--seconds"), out var s) ? s : 120;
        ApplicationConfiguration.Initialize();
        Application.Run(new TargetForm(outPath, TimeSpan.FromSeconds(seconds)));
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
    private readonly TextBox _box = new() { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14) };
    private int _enter;
    private int _backspace;

    public TargetForm(string outPath, TimeSpan lifetime)
    {
        _outPath = outPath;
        Text = "WhisperLoom TestTarget";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(640, 320);
        TopMost = true;
        Controls.Add(_box);
        _box.KeyDown += OnKeyDown;
        _box.TextChanged += (_, _) => Save();
        Activated += (_, _) => Save();
        Shown += (_, _) => _box.Focus();
        var timer = new System.Windows.Forms.Timer { Interval = (int)lifetime.TotalMilliseconds };
        timer.Tick += (_, _) => Close();
        timer.Start();
        Save();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
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
        var json = JsonSerializer.Serialize(new { text = _box.Text, enter = _enter, backspace = _backspace, hwnd = Handle.ToInt64() });
        var tmp = _outPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _outPath, overwrite: true);
    }
}
