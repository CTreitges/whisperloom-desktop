using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;

namespace WhisperLoom.App.Tests.Features;

/// <summary>Stufen-Wähler (W2-OUT): jede Taste und der Klick, headless am echten Steuerelement.</summary>
public sealed class StagePickerTest
{
    private static readonly (string, string)[] Stages =
        [("off", "Aus"), ("polish", "Glätten"), ("beautify", "Verschönern"), ("summarize", "Zusammenfassen"), ("readable", "Lesbar")];

    private readonly List<string> _chosen = [];
    private int _cancelled;

    private (Window Window, StagePickerViewModel Vm) Show(string current = "polish")
    {
        var vm = new StagePickerViewModel(Stages, current);
        vm.Chosen += _chosen.Add;
        vm.Cancelled += () => _cancelled++;
        var picker = new StagePicker { DataContext = vm };
        var window = new Window { Content = picker, Width = 300, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        picker.Focus();
        return (window, vm);
    }

    private static void Press(Window w, Key key, RawInputModifiers mods = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None) =>
        w.KeyPress(key, mods, physical, null);

    [AvaloniaFact]
    public void Zeigt_aktuelle_Stufe_und_markiert_sie()
    {
        var (w, vm) = Show("beautify");
        Assert.Equal("beautify", vm.HighlightedOption!.Key);
        Assert.True(vm.Options.Single(o => o.Key == "beautify").IsCurrent);
        Assert.Equal(["1", "2", "3", "4", "5"], vm.Options.Select(o => o.NumberText));
        w.Close();
    }

    [AvaloniaFact]
    public void Pfeile_bewegen_rundum()
    {
        var (w, vm) = Show("off");
        Press(w, Key.Down, physical: PhysicalKey.ArrowDown);
        Assert.Equal("polish", vm.HighlightedOption!.Key);
        Press(w, Key.Up, physical: PhysicalKey.ArrowUp);
        Press(w, Key.Up, physical: PhysicalKey.ArrowUp);
        Assert.Equal("readable", vm.HighlightedOption!.Key);
        Assert.Empty(_chosen);
        w.Close();
    }

    [AvaloniaFact]
    public void Tab_und_Umschalt_Tab_bleiben_im_Waehler()
    {
        var (w, vm) = Show("polish");
        Press(w, Key.Tab, physical: PhysicalKey.Tab);
        Assert.Equal("beautify", vm.HighlightedOption!.Key);
        Press(w, Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab);
        Press(w, Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab);
        Assert.Equal("off", vm.HighlightedOption!.Key);
        Assert.IsType<StagePicker>(w.FocusManager!.GetFocusedElement());
        w.Close();
    }

    [AvaloniaFact]
    public void Enter_waehlt_die_markierte()
    {
        var (w, vm) = Show("polish");
        Press(w, Key.Down, physical: PhysicalKey.ArrowDown);
        Press(w, Key.Enter, physical: PhysicalKey.Enter);
        Assert.Equal(["beautify"], _chosen);
        Assert.Equal("beautify", vm.Current);
        Assert.True(vm.Options.Single(o => o.Key == "beautify").IsCurrent);
        w.Close();
    }

    [AvaloniaFact]
    public void Ziffer_waehlt_sofort()
    {
        var (w, _) = Show();
        Press(w, Key.D4, physical: PhysicalKey.Digit4);
        Press(w, Key.NumPad1, physical: PhysicalKey.NumPad1);
        Press(w, Key.D9, physical: PhysicalKey.Digit9); // gibt es nicht: nichts
        Assert.Equal(["summarize", "off"], _chosen);
        w.Close();
    }

    [AvaloniaFact]
    public void Esc_bricht_ab()
    {
        var (w, vm) = Show();
        Press(w, Key.Escape, physical: PhysicalKey.Escape);
        Assert.Equal(1, _cancelled);
        Assert.Empty(_chosen);
        Assert.Equal("polish", vm.Current);
        w.Close();
    }

    [AvaloniaFact]
    public void Klick_waehlt()
    {
        var (w, _) = Show();
        var button = w.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is StageOption { Key: "readable" });
        button.Command!.Execute(button.CommandParameter);
        Assert.Equal(["readable"], _chosen);
        w.Close();
    }

    [AvaloniaFact]
    public void Ab_der_zehnten_Stufe_keine_Ziffer()
    {
        var vm = new StagePickerViewModel(Enumerable.Range(1, 11).Select(i => ($"s{i}", $"Stufe {i}")));
        Assert.Equal("", vm.Options[9].NumberText);
        Assert.False(vm.ChooseNumber(10));
        Assert.Equal(0, vm.Highlighted);
    }
}
