using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Tray;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>Headless-Tests der Fenster. Laufen alle auf dem Avalonia-UI-Thread; die Sprache wird je Test gesetzt.</summary>
public class UiTest
{
    private const string SecretValue = "test-key-not-real-3";

    internal static IEnumerable<string> TextsOf(Window window) =>
        window.GetVisualDescendants().Concat(window.GetLogicalDescendants()).Distinct().SelectMany(v => v switch
        {
            TextBox t => [t.Text ?? "", t.PlaceholderText ?? ""],
            TextBlock t => [t.Text ?? "", .. t.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text ?? "") ?? []],
            ContentControl { Content: string s } => [s],
            HeaderedItemsControl { Header: string h } => [h],
            HeaderedContentControl { Header: string h } => [h],
            _ => Array.Empty<string>(),
        });

    private static async Task<(SettingsWindow Window, SettingsViewModel Vm, MemorySecretStore Store, ControllerFixture F)> OpenSettings()
    {
        var f = new ControllerFixture();
        var store = new MemorySecretStore();
        store.Values[SecretNames.Stt] = SecretValue;
        store.Values[SecretNames.Llm] = SecretValue;
        var vm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker());
        await vm.LoadKeysAsync();
        var window = new SettingsWindow(vm);
        window.Show();
        return (window, vm, store, f);
    }

    [AvaloniaFact]
    public async Task Key_fields_show_only_mask_and_never_the_value()
    {
        Loc.Instance.SetLanguage("de");
        var (window, vm, _, f) = await OpenSettings();
        using (f)
        {
            foreach (var tab in window.GetLogicalDescendants().OfType<TabItem>().ToList())
            {
                tab.IsSelected = true;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.DoesNotContain(TextsOf(window), t => t.Contains(SecretValue, StringComparison.Ordinal));
            }

            var masks = window.GetLogicalDescendants().OfType<TextBlock>().Where(t => t.Name == "MaskText").ToList();
            Assert.NotEmpty(masks);
            Assert.Contains(masks, m => m.Text == KeyFieldViewModel.Mask);
            Assert.All(masks.Where(m => vm.SttKey.IsSet && m.DataContext == vm.SttKey), m => Assert.Matches("^•+$", m.Text!));

            // Kein Aufdecken: Ersetzen-Feld maskiert, RevealPassword aus, kein Knopf zum Anzeigen.
            var boxes = window.GetLogicalDescendants().OfType<TextBox>().Where(b => b.Name == "NewKeyBox").ToList();
            Assert.All(boxes, b => { Assert.Equal('•', b.PasswordChar); Assert.False(b.RevealPassword); });
            var buttonTexts = window.GetLogicalDescendants().OfType<Button>().Select(b => b.Content as string ?? "").ToList();
            Assert.DoesNotContain(buttonTexts, t => t.Contains("anzeig", StringComparison.OrdinalIgnoreCase)
                || t.Contains("show", StringComparison.OrdinalIgnoreCase) || t.Contains("reveal", StringComparison.OrdinalIgnoreCase)
                || t.Contains("aufdeck", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(["Ersetzen", "Löschen", "Zugang prüfen"], buttonTexts.Where(t => t is "Ersetzen" or "Löschen" or "Zugang prüfen").Distinct());
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Replacing_a_key_stores_it_and_clears_the_input()
    {
        Loc.Instance.SetLanguage("de");
        var (window, vm, store, f) = await OpenSettings();
        using (f)
        {
            vm.SttKey.ReplaceCommand.Execute(null);
            vm.SttKey.NewValue = "test-key-not-real-4";
            await vm.SttKey.SaveKeyCommand.ExecuteAsync(null);
            Assert.Equal("test-key-not-real-4", store.Values[SecretNames.Stt]);
            Assert.Equal("", vm.SttKey.NewValue);
            Assert.DoesNotContain(TextsOf(window), t => t.Contains("test-key-not-real-4", StringComparison.Ordinal));

            await vm.LlmKey.DeleteCommand.ExecuteAsync(null);
            Assert.False(store.Values.ContainsKey(SecretNames.Llm));
            Assert.Equal("nicht gesetzt", vm.LlmKey.MaskText);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Stage_view_shows_raw_and_result_side_by_side_in_large_font()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = new StageWindow(new StageViewModel(f.Controller)) { Width = 1200, Height = 700 };
        window.Show();
        f.Controller.LastRaw = "roh";
        f.Controller.LastResult = "fertig";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var raw = window.FindControl<SelectableTextBlock>("RawText")!;
        var result = window.FindControl<SelectableTextBlock>("ResultText")!;
        Assert.Equal("roh", raw.Text);
        Assert.Equal("fertig", result.Text);
        Assert.True(raw.FontSize >= 28 && result.FontSize >= 28);
        Assert.Equal(32, result.FontSize);
        var rawPos = raw.TranslatePoint(new(0, 0), window)!.Value;
        var resultPos = result.TranslatePoint(new(0, 0), window)!.Value;
        Assert.True(resultPos.X > rawPos.X + 300, $"raw x={rawPos.X} result x={resultPos.X}");
        Assert.InRange(resultPos.Y - rawPos.Y, -4, 4);
        window.Close();
    }

    [AvaloniaFact]
    public void Ctrl_plus_minus_zooms_and_f11_toggles_fullscreen()
    {
        using var f = new ControllerFixture();
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm);
        window.Show();

        window.KeyPress(Key.OemPlus, RawInputModifiers.Control, PhysicalKey.Equal, "+");
        Assert.Equal(36, vm.FontSize);
        window.KeyPress(Key.Add, RawInputModifiers.Control, PhysicalKey.NumPadAdd, "+");
        Assert.Equal(40, vm.FontSize);
        window.KeyPress(Key.OemMinus, RawInputModifiers.Control, PhysicalKey.Minus, "-");
        Assert.Equal(36, vm.FontSize);
        Assert.Equal(36, window.FindControl<SelectableTextBlock>("ResultText")!.FontSize);
        Assert.Equal(36, Services.SettingsStore.Desktop(f.Store.Load()).StageFontSize);

        window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        window.KeyPress(Key.F11, RawInputModifiers.None, PhysicalKey.F11, null);
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.Close();
    }

    [AvaloniaFact]
    public void Stage_view_shows_stage_language_and_recording_state()
    {
        Loc.Instance.SetLanguage("en");
        var settings = new WhisperLoomSettings { Language = "en", Refine = new RefineSettings { Stage = "prompt", PromptEnabled = true } };
        using var f = new ControllerFixture(settings);
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm);
        window.Show();
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(65);
        vm.Tick();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.Equal("Prompt", vm.StageText);
        Assert.Equal("EN", vm.LanguageText);
        Assert.Equal("01:05", window.FindControl<TextBlock>("Timer")!.Text);
        Assert.Contains("recording", window.FindControl<Avalonia.Controls.Shapes.Ellipse>("RecordDot")!.Classes);
        Assert.True(window.FindControl<ProgressBar>("LevelBar")!.Value > 0.5);
        window.Close();
    }

    [AvaloniaFact]
    public void Tray_menu_has_all_entries()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var opened = new List<string>();
        var tray = new TrayMenu(f.Controller, opened.Add);
        var headers = tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Select(i => i.Header).ToList();
        Assert.Equal(["Aufnahme starten", "Stufe: Aus", "Bühnenansicht", "Verlauf", "Einstellungen", "Beenden"], headers);
        var stages = tray.Menu.Items.OfType<NativeMenuItem>().ElementAt(1).Menu!.Items.OfType<NativeMenuItem>().Select(i => i.Header);
        Assert.Equal(["Aus", "Glätten", "Verschönern", "Zusammenfassen"], stages);

        foreach (var item in tray.Menu.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).Skip(2))
        {
            item.Command!.Execute(null);
        }
        Assert.Equal(["stage", "history", "settings", "quit"], opened);

        f.Controller.Start();
        Assert.Equal("Aufnahme beenden", tray.Menu.Items.OfType<NativeMenuItem>().First().Header);
    }

    [AvaloniaFact]
    public void Switching_language_updates_open_windows()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = new StageWindow(new StageViewModel(f.Controller));
        window.Show();
        Assert.Contains("Rohtext", TextsOf(window));
        Assert.Contains("Ergebnis", TextsOf(window));

        Loc.Instance.SetLanguage("en");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Contains("Raw text", TextsOf(window));
        Assert.Contains("Result", TextsOf(window));
        Assert.DoesNotContain("Rohtext", TextsOf(window));
        Assert.Equal("WhisperLoom — Stage", window.Title);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Settings_save_switches_ui_language_and_keeps_keys_out_of_settings_json()
    {
        Loc.Instance.SetLanguage("de");
        var (window, vm, _, f) = await OpenSettings();
        using (f)
        {
            vm.UiLanguage = vm.UiLanguages.First(c => c.Value == "en");
            vm.Save();
            Assert.Equal("en", Loc.Instance.Language);
            Assert.Equal("WhisperLoom — Settings", window.Title);
            var json = await File.ReadAllTextAsync(f.Store.FilePath);
            Assert.Contains("\"uiLanguage\": \"en\"", json);
            Assert.DoesNotContain(SecretValue, json);
            window.Close();
        }
        Loc.Instance.SetLanguage("de");
    }
}
