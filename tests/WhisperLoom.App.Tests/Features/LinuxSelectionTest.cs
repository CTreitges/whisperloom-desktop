using System.ComponentModel;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Linux;
using WhisperLoom.App.Secrets;
using WhisperLoom.Core.Contracts;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Linux-Weg von „Markierung verarbeiten“ (W2-SEL): Primärauswahl per wl-paste (Wayland) bzw. xclip/xsel (X11), fehlendes
/// Werkzeug → klarer Hinweis, Stufe per <c>--stage</c> oder Wähler-Fenster, Ergebnis in die Ausgabe (Zwischenablage).
/// </summary>
public sealed class LinuxSelectionTest
{
    private sealed class Runner : IProcessRunner
    {
        public Dictionary<string, ProcessResult> Installed { get; } = [];

        public List<string> Calls { get; } = [];

        public Task<ProcessResult> RunAsync(string file, IReadOnlyList<string> args, string? stdin, CancellationToken ct)
        {
            Calls.Add(file + " " + string.Join(" ", args));
            return Installed.TryGetValue(file, out var r) ? Task.FromResult(r) : throw new Win32Exception(2, "nicht gefunden");
        }

        public Task<int> RunUntilExitAsync(string file, IReadOnlyList<string> args, string stdin, TimeSpan timeout, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static Func<string, string?> Env(bool wayland) => name => name == "WAYLAND_DISPLAY" && wayland ? "wayland-0" : null;

    [Fact]
    public async Task Wayland_liest_mit_wl_paste_primary()
    {
        var runner = new Runner { Installed = { ["wl-paste"] = new ProcessResult(0, "markiert", "") } };
        var read = await new PrimarySelection(runner, Env(true)).ReadAsync(CancellationToken.None);
        Assert.Equal(new PrimaryRead("markiert"), read);
        Assert.Equal(["wl-paste --primary --no-newline"], runner.Calls);
    }

    [Fact]
    public async Task X11_nimmt_xclip_sonst_xsel()
    {
        var runner = new Runner { Installed = { ["xsel"] = new ProcessResult(0, "aus xsel", "") } };
        var read = await new PrimarySelection(runner, Env(false)).ReadAsync(CancellationToken.None);
        Assert.Equal("aus xsel", read.Text);
        Assert.Equal(["xclip -o -selection primary", "xsel -o -p"], runner.Calls);
    }

    [Fact]
    public async Task Leere_Auswahl_ist_leer_kein_Werkzeug_ist_ein_Hinweis()
    {
        var empty = new Runner { Installed = { ["wl-paste"] = new ProcessResult(1, "", "No selection") } };
        Assert.Equal(new PrimaryRead(""), await new PrimarySelection(empty, Env(true)).ReadAsync(CancellationToken.None));

        Assert.Equal("wl-clipboard (wl-paste)", (await new PrimarySelection(new Runner(), Env(true)).ReadAsync(CancellationToken.None)).MissingTool);
        Assert.Equal("xclip", (await new PrimarySelection(new Runner(), Env(false)).ReadAsync(CancellationToken.None)).MissingTool);
    }

    private static (ControllerFixture F, List<string> Notes, LinuxSelectionFlow Flow) Flow(Runner runner, Func<StagePickerViewModel, Task<string?>>? pick = null)
    {
        var f = new ControllerFixture();
        var notes = new List<string>();
        return (f, notes, new LinuxSelectionFlow(f.Controller, new PrimarySelection(runner, Env(false)), pick ?? (_ => Task.FromResult<string?>(null)), notes.Add));
    }

    [AvaloniaFact]
    public async Task Mit_Stufe_direkt_ins_Ergebnis_ohne_Waehler()
    {
        var (f, notes, flow) = Flow(new Runner { Installed = { ["xclip"] = new ProcessResult(0, "linux text", "") } },
            _ => throw new InvalidOperationException("kein Wähler bei --stage"));
        using (f)
        {
            Assert.Equal("done", await flow.RunAsync("summarize"));
            Assert.Equal(["[summarize] linux text"], f.Sink.Written);
            Assert.Empty(notes);
            Assert.Equal(HistoryEntry.SourceSelection, (await f.History.ListAsync(CancellationToken.None))[0].Source);
        }
    }

    [AvaloniaFact]
    public async Task Ohne_Stufe_Waehler_ohne_Anweisung_Abbruch_aendert_nichts()
    {
        StagePickerViewModel? shown = null;
        var (f, _, flow) = Flow(new Runner { Installed = { ["xclip"] = new ProcessResult(0, "t", "") } }, p =>
        {
            shown = p;
            return Task.FromResult<string?>(null);
        });
        using (f)
        {
            Assert.Equal("cancelled", await flow.RunAsync(null));
            Assert.Equal(["polish", "beautify", "summarize"], shown!.Options.Select(o => o.Key));
            Assert.Empty(f.Sink.Written);
        }
    }

    [AvaloniaFact]
    public async Task Fehlendes_Werkzeug_und_leere_Auswahl_melden_sich()
    {
        Loc.Instance.SetLanguage("de");
        var (f, notes, flow) = Flow(new Runner());
        using (f)
        {
            Assert.Equal("no-tool", await flow.RunAsync("polish"));
            Assert.Equal("Markierung nicht lesbar: xclip fehlt – bitte installieren", notes[^1]);
        }
        var (g, notes2, flow2) = Flow(new Runner { Installed = { ["xclip"] = new ProcessResult(0, " ", "") } });
        using (g)
        {
            Assert.Equal("empty", await flow2.RunAsync("polish"));
            Assert.Equal(Loc.Instance["Selection_Empty"], notes2[^1]);
            Assert.Empty(g.Sink.Written);
        }
    }
}
