using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Platform.Windows.Input;
using WhisperLoom.App.Platform.Windows.TextOutput;
using WhisperLoom.App.Tests.Windows;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Output;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Regressionstests zu den Review-Befunden W2-OUT (Prüfer-Subagent): je Befund ein Fall, ohne den Fix rot.</summary>
public sealed class ReviewW2OutTest
{
    private sealed class Throwing(Exception e) : IFocusSource
    {
        public FocusInfo? Focused() => throw e;
    }

    [Theory]
    [InlineData("notimpl")]
    [InlineData("nullref")]
    public async Task R1_fremder_COM_Fehler_der_Fokusquelle_ist_unbekannt(string kind)
    {
        Exception e = kind == "notimpl" ? new NotImplementedException() : new NullReferenceException();
        Assert.Equal(FocusKind.Unknown, await new FocusProbe(new Throwing(e)).ProbeAsync(CancellationToken.None));
        Assert.Equal(FocusKind.Unknown,
            await new FocusProbe(new Throwing(new System.Runtime.InteropServices.COMException()), fallback: new Throwing(e)).ProbeAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("Pasted", "History_Status_inserted")]
    [InlineData("NoTextField", "Output_Hint_Copied")]
    [InlineData("PasswordField", "Output_Hint_Password")]
    [InlineData("busy", "History_Status_busy")]
    [InlineData("error", "History_Status_insertFailed")]
    [InlineData("Empty", "History_Status_insertFailed")]
    public void R2_Kopiert_nur_wenn_der_Text_in_der_Zwischenablage_liegt(string code, string key) =>
        Assert.Equal(key, HistoryViewModel.InsertStatusKey(code));

    [AvaloniaFact]
    public async Task R3_Enter_auf_einem_Knopf_loest_den_Knopf_aus_statt_einzufuegen()
    {
        using var f = new ControllerFixture();
        var at = f.Now;
        await f.History.AddAsync(new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 1000, "roh", "Text", Refinement.Off, "x"), CancellationToken.None);
        var inserted = new List<string>();
        var vm = new HistoryViewModel(f.History, new HistoryActions(_ => Task.FromResult(true), t =>
        {
            inserted.Add(t);
            return Task.FromResult("Pasted");
        }, null, f.Controller.EditHistoryAsync, []));
        await vm.LoadAsync();
        var w = new HistoryWindow(vm);
        w.Show();
        vm.AskClear();
        Dispatcher.UIThread.RunJobs();
        var confirm = w.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ClearConfirmButton");
        confirm.Focus();
        w.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.Empty(inserted);
        Assert.Empty(await f.History.ListAsync(CancellationToken.None));
        w.Close();
    }

    [Fact]
    public async Task R4_belegte_Zwischenablage_nach_dem_Einfuegen_ist_kein_Fehler()
    {
        var rig = new InsertRig();
        rig.Focus.Kind = FocusKind.Unknown;
        var busy = new PublicBusyClipboard(rig.Clipboard);
        var inserter = new TextInserter(busy, rig.Keys, rig.Windows, TimeSpan.Zero, rig.Focus, _ => Task.CompletedTask);
        Assert.Equal(InsertOutcome.Pasted, await inserter.InsertAsync(42, "Hallo", InsertMode.Paste, false, CancellationToken.None));
        Assert.Equal("Hallo", rig.Keys.Target);
        Assert.Equal("Hallo", rig.Clipboard.Text); // privat gesetzt und eingefügt – liegt weiter drin
        Assert.Equal(InsertOutcome.Typed, await inserter.InsertAsync(42, "Tipp", InsertMode.Type, false, CancellationToken.None));
    }

    /// <summary>Öffentliches Setzen ist dauerhaft belegt, privates (zum Einfügen) geht.</summary>
    private sealed class PublicBusyClipboard(FakeClipboard inner) : IClipboardAdapter
    {
        public uint SequenceNumber => inner.SequenceNumber;

        public ClipboardSnapshot Save() => inner.Save();

        public void SetText(string text, bool @private)
        {
            if (!@private)
            {
                throw new IOException("belegt");
            }
            inner.SetText(text, @private);
        }

        public void Restore(ClipboardSnapshot snapshot) => inner.Restore(snapshot);

        public string? GetText() => inner.GetText();
    }
}
