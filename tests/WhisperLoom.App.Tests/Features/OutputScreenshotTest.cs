using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Tests.Features;

/// <summary>
/// Headless-Screenshots für W2-OUT (Abnahme 11): Verlauf (Liste, Detail, Suche, Bearbeiten, Stufen-Wähler), Hinweise (kopiert,
/// Passwortfeld), Overlay mit Stufen-Wähler, Ausgabe- und Datenschutz-Seite. Nur mit <c>WLD_SCREENSHOT_DIR</c>; ≤ 1200 px.
/// </summary>
public sealed class OutputScreenshotTest
{
    private static string? Dir => Environment.GetEnvironmentVariable("WLD_SCREENSHOT_DIR");

    private static void Save(Window window, string name, bool show = true)
    {
        if (show)
        {
            window.Show();
        }
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick(30);
        Dispatcher.UIThread.RunJobs();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(frame.PixelSize.Width <= 1200, $"Breite {frame.PixelSize.Width}");
        Directory.CreateDirectory(Dir!);
        frame.Save(Path.Combine(Dir!, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static T OnDark<T>(T window) where T : Window
    {
        window.Background = (Avalonia.Media.IBrush)Avalonia.Application.Current!.FindResource("WlBackground")!;
        return window;
    }

    private static async Task Fill(ControllerFixture f, DateTimeOffset now)
    {
        (string Raw, string Text, string Stage, double Hours)[] items =
        [
            ("ähm also ich komme morgen etwas später ins büro weil der zug ausfällt", "Ich komme morgen etwas später ins Büro, weil der Zug ausfällt.", "polish", 0.2),
            ("schreib bitte an das team dass das review auf donnerstag verschoben wird", "Hallo zusammen,\n\ndas Review wird auf Donnerstag verschoben.\n\nViele Grüße", "beautify", 1.5),
            ("einkaufsliste milch eier brot und kaffee", "Einkaufsliste: Milch, Eier, Brot und Kaffee.", "polish", 5),
            ("kurze zusammenfassung vom call kunde will angebot bis freitag", "Kunde wünscht Angebot bis Freitag.", "summarize", 26),
            ("notiz an mich zahnarzt termin verschieben", "Notiz: Zahnarzttermin verschieben.", "off", 50),
        ];
        foreach (var (raw, text, stage, hours) in items)
        {
            var at = now.AddHours(-hours);
            await f.History.AddAsync(new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 6200, raw, text,
                new Refinement(RefineModes.FromKey(stage)), "OpenAI · whisper-1", stage == "off" ? null : "gpt-5-mini"), CancellationToken.None);
        }
    }

    [AvaloniaFact]
    public async Task Render_history_hints_overlay_and_output_pages()
    {
        if (Dir is null)
        {
            Assert.Skip("WLD_SCREENSHOT_DIR nicht gesetzt");
            return;
        }
        foreach (var (variant, theme) in new[] { ("dark", ThemeVariant.Dark), ("light", ThemeVariant.Light) })
        {
            Loc.Instance.SetLanguage("de");
            var now = DateTimeOffset.Now;
            using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });
            f.Now = now;
            await Fill(f, now);
            var actions = new HistoryActions(_ => Task.FromResult(true), _ => Task.FromResult("Pasted"),
                (id, stage) => f.Controller.ReprocessVersionAsync(id, stage), f.Controller.EditHistoryAsync,
                [.. RefineModes.SettingsFor(true).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()]))]);
            var vm = new HistoryViewModel(f.History, actions, () => now);
            await vm.LoadAsync();
            var w = new HistoryWindow(vm) { RequestedThemeVariant = theme };
            w.Show();
            Save(w, $"out-01-verlauf-liste-{variant}.png", show: false);

            vm.Selected = vm.Rows[1];
            await vm.Detail!.RequestReprocessAsync("summarize");
            await vm.Detail.Running;
            await vm.CopySelected();
            Save(w, $"out-02-verlauf-detail-fassungen-{variant}.png", show: false);

            vm.Detail.ToggleStagePicker();
            Save(w, $"out-03-verlauf-stufen-waehler-{variant}.png", show: false);
            vm.Detail.ToggleStagePicker();

            vm.Detail.BeginEdit();
            Save(w, $"out-04-verlauf-bearbeiten-{variant}.png", show: false);
            vm.Detail.CancelEdit();

            vm.Search = "zug";
            Save(w, $"out-05-verlauf-suche-{variant}.png", show: false);
            vm.Search = "";
            await vm.DeleteSelected();
            Save(w, $"out-06-verlauf-geloescht-rueckgaengig-{variant}.png", show: false);
            w.Close();

            if (variant == "dark")
            {
                var copied = new HintWindow();
                copied.SetContent("toast", "WhisperLoom", Loc.Instance["Output_Hint_Copied"]);
                Save(OnDark(copied), "out-07-hinweis-kopiert.png");
                copied.Close();
                var password = new HintWindow();
                password.SetContent("toast", "WhisperLoom", Loc.Instance["Output_Hint_Password"]);
                Save(OnDark(password), "out-08-hinweis-passwortfeld.png");
                password.Close();

                var overlay = new OverlayViewModel(() => { }, () => { }, () => { })
                {
                    Stage = Loc.Instance["Stage_polish"],
                    Elapsed = TimeSpan.FromSeconds(9),
                    Level = 0.5,
                    StagePicker = new WhisperLoom.App.Design.StagePickerViewModel(
                        RefineModes.SettingsFor(true).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])), "polish"),
                    IsStagePickerOpen = true,
                };
                var ow = OnDark(new OverlayWindow(overlay));
                Save(ow, "out-09-overlay-stufen-waehler.png");
                ow.Close();
            }

            var svm = new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker());
            var settings = new SettingsWindow(svm) { Width = 900, Height = 720, RequestedThemeVariant = theme };
            settings.Show();
            Settings.SettingsUi.Show(settings, "output");
            Save(settings, $"out-10-einstellungen-ausgabe-{variant}.png", show: false);
            Settings.SettingsUi.Show(settings, "privacy");
            await Fill(f, now.AddDays(-7));
            await Fill(f, now.AddDays(-14));
            await Fill(f, now.AddDays(-21));
            svm.AutoSave = true;
            svm.HistorySizeChoice = 10;
            await svm.HistorySizePending;
            Dispatcher.UIThread.RunJobs();
            Save(settings, $"out-11-einstellungen-verlauf-verkleinern-{variant}.png", show: false);
            settings.Close();
        }
    }
}
