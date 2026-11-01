using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Sidecar;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Management;
using WhisperLoom.App.Tests.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.App.Views.Help;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Testing;

namespace WhisperLoom.App.Tests.Help;

/// <summary>Eine aufgebaute Szene: Fenster, aufzunehmender Bereich, Aufräumen.</summary>
internal sealed record HelpScene(Window Window, Control Target, IDisposable? Cleanup, bool CropToContent = false);

/// <summary>
/// Szenen der Hilfe-Screenshots (<c>Assets/Help/screens.json</c>, Feld <c>scene</c>) mit Fake-Daten: keine echten Diktate,
/// nur Test-Keys (<c>test-key-not-real-n</c>), Beispiel-Firma. Einstellungen = nur der Seitenbereich (ohne Seitenleiste).
/// </summary>
internal static class HelpShotScenes
{
    public const double Width = 900;

    public static async Task<HelpScene> BuildAsync(HelpShot shot, string lang, ThemeVariant theme)
    {
        var parts = shot.Scene.Split(':');
        return parts[0] switch
        {
            "settings" => await SettingsAsync(parts[1], parts.Length > 2 ? parts[2] : null, lang, theme, shot.Height > 0 ? shot.Height : 640),
            "setup" => Setup(theme),
            "stage" => await StageAsync(lang, theme, parts.Length > 1 && parts[1] == "error"),
            "picker" => Picker(theme),
            "history" => await HistoryAsync(lang, theme),
            _ => throw new InvalidOperationException($"Unbekannte Szene: {shot.Scene}"),
        };
    }

    private static async Task<HelpScene> SettingsAsync(string page, string? variant, string lang, ThemeVariant theme, int height)
    {
        ManagementRig? rig = page == "company" ? new ManagementRig(new WhisperLoomSettings { Language = lang }, uiThread: true) : null;
        var f = rig?.F ?? new ControllerFixture(page is "providers" or "localmodels" ? LocalDemo(lang) : new WhisperLoomSettings { Language = lang, Engine = "online" });
        if (variant == "managed")
        {
            Managed(f);
        }
        f.Controller.Settings.Selection.FloatingButton = true;
        var store = new MemorySecretStore();
        store.Values[SecretNames.Stt] = "test-key-not-real-9";
        store.Values["custom:labor"] = "test-key-not-real-8";
        var vm = new SettingsViewModel(f.Controller, store, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = rig?.Service };
        await vm.LoadKeysAsync();
        SettingsShellViewModel.LastPage = page;
        var window = new SettingsWindow(new SettingsShellViewModel(vm)) { Width = Width, Height = height, RequestedThemeVariant = theme };
        window.Show();
        Settings.SettingsUi.Show(window, page);
        if (window.Shell!.SelectedPage is ProvidersPageViewModel providers)
        {
            await providers.LoadKeysAsync();
        }
        if (window.Shell.SelectedPage is CompanyPageViewModel company)
        {
            company.OpenJoinCommand.Execute(null);
            company.Join!.Server = "https://wl.example.com";
            company.Join.Code = "ABCD-EFGH-JKMN-PQRS";
        }
        Dispatcher.UIThread.RunJobs();
        SettingsShellViewModel.LastPage = "general";
        return new HelpScene(window, window.FindControl<ContentControl>("PageHost")!, (IDisposable?)rig ?? f, CropToContent: true);
    }

    /// <summary>Firma mit festen Erkennungs-Zeilen und einer Vorgabe (wie SettingsScreenshotTest).</summary>
    private static void Managed(ControllerFixture f)
    {
        f.Controller.Settings.Management.TenantName = "Beispiel GmbH";
        f.Controller.Settings.Stt.Model = "whisper-1";
        f.Controller.Policy = new Core.Settings.PolicySettings
        {
            Locked = ["stt.providerId", "secrets.stt", "stt.baseUrl", "stt.readTimeoutSec"],
            Defaults = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["stt.model"] = System.Text.Json.JsonSerializer.SerializeToElement("whisper-1"),
            },
        };
    }

    private static WhisperLoomSettings LocalDemo(string lang)
    {
        var s = new WhisperLoomSettings { Language = lang, Engine = "offline" };
        s.Providers.Custom.Add(new CustomProvider { Id = "labor", Name = "Labor-Server", Protocol = "openai", BaseUrl = "https://llm.labor.example/v1", Models = ["whisper-large-v3"] });
        s.Offline.CustomModels.Add(new CustomWhisperModel { Name = "whisper-de-finetune", Path = "D:/Modelle/ggml-de-finetune.bin" });
        LocalSettings.Set(s, new LocalSettings
        {
            TextEngine = "bundled",
            Servers = [new LocalServer { Name = "LM Studio", Kind = "text", BaseUrl = "http://127.0.0.1:1234/v1", Model = "qwen2.5-7b-instruct" }],
        });
        return s;
    }

    private static HelpScene Setup(ThemeVariant theme)
    {
        var rig = new ManagementRig(uiThread: true);
        var settings = new SettingsViewModel(rig.F.Controller, rig.Secrets, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = rig.Service };
        var vm = new SetupViewModel(settings, new SetupState(rig.F.DataDir), new FakeAutostart(), null, null, true, "whisperloom toggle", rig.Service);
        var window = new SetupWindow(vm) { Width = 760, Height = 560, RequestedThemeVariant = theme };
        window.Show();
        return new HelpScene(window, (Control)window.Content!, rig);
    }

    private static async Task<HelpScene> StageAsync(string lang, ThemeVariant theme, bool error)
    {
        var f = new ControllerFixture(new WhisperLoomSettings { Language = lang, Refine = new RefineSettings { Stage = "polish" } });
        f.Pipeline.RawText = lang == "de"
            ? "ähm also das meeting ist am donnerstag um zehn und bitte bringt die zahlen mit"
            : "um so the meeting is on thursday at ten and please bring the numbers";
        var vm = new StageViewModel(f.Controller);
        // Die Bühne ist immer dunkel (Beamer); hell würde nur Steuerelemente der Vorlage umfärben.
        var window = new StageWindow(vm) { Width = 900, Height = 440, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        if (error)
        {
            f.Pipeline.Failure = FakeFailure.SttNetwork;
        }
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(6);
        await f.Controller.StopAsync();
        if (!error)
        {
            f.Controller.LastResult = lang == "de"
                ? "Das Meeting ist am Donnerstag um 10 Uhr. Bitte bringt die Zahlen mit."
                : "The meeting is on Thursday at 10. Please bring the numbers.";
        }
        vm.Tick();
        return new HelpScene(window, (Control)window.Content!, f);
    }

    /// <summary>Dokument mit markiertem Satz, darunter der Stufen-Wähler an der Schreibmarke (wie SelectionScreenshotTest).</summary>
    private static HelpScene Picker(ThemeVariant theme)
    {
        var app = Application.Current!;
        IBrush Brush(string key) => (IBrush)app.FindResource(theme, key)!;
        var stages = new[] { RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize }
            .Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])).ToList();
        stages.Add(("instruction", Loc.Instance["Selection_Instruction"]));
        var picker = new StagePickerViewModel(stages, "polish");
        picker.Move(1);
        var pickerWindow = new SelectionPickerWindow(noActivate: false) { DataContext = picker, RequestedThemeVariant = theme };
        var pickerContent = (Control)pickerWindow.Content!;
        pickerWindow.Content = null;
        pickerContent.Width = 290;
        pickerContent.DataContext = picker;
        pickerContent.Name = "Picker";
        var de = Loc.Instance.Language == "de";
        var selected = new Border
        {
            Name = "Selected",
            Background = Brush("Loom.Selection"),
            Child = new TextBlock { Text = de ? "der bericht ist fertig ich schick ihn dir morgen" : "the report is done ill send it to you tomorrow", FontSize = 16, Foreground = Brush("Loom.OnSelection") },
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 34 };
        line.Children.Add(new TextBlock { Text = de ? "Hallo Anna," : "Hi Anna,", FontSize = 16, Foreground = Brush("Loom.Text") });
        line.Children.Add(selected);
        var doc = new Border
        {
            Background = Brush("Loom.Surface"),
            BorderBrush = Brush("Loom.Separator"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 20),
            Width = 620,
            Child = new StackPanel { Spacing = 10, Children = { line, new TextBlock { Text = de ? "Viele Grüße" : "Best regards", FontSize = 16, Foreground = Brush("Loom.Text") } } },
        };
        var canvas = new Canvas { Width = 680, Height = 330, Background = Brush("Loom.Background") };
        Canvas.SetLeft(doc, 20);
        Canvas.SetTop(doc, 20);
        canvas.Children.Add(doc);
        Canvas.SetLeft(pickerContent, 150);
        Canvas.SetTop(pickerContent, 96);
        canvas.Children.Add(pickerContent);
        var window = new Window { Width = 680, Height = 330, Content = canvas, RequestedThemeVariant = theme };
        window.Show();
        return new HelpScene(window, canvas, null);
    }

    private static async Task<HelpScene> HistoryAsync(string lang, ThemeVariant theme)
    {
        var now = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(1));
        var f = new ControllerFixture(new WhisperLoomSettings { Language = lang, Refine = new RefineSettings { Stage = "polish" } }) { Now = now };
        var de = lang == "de";
        (string Raw, string Text, string Stage, double Hours)[] items = de
            ?
            [
                ("ähm also ich komme morgen etwas später weil der zug ausfällt", "Ich komme morgen etwas später, weil der Zug ausfällt.", "polish", 0.2),
                ("schreib bitte an das team dass das review auf donnerstag verschoben wird", "Hallo zusammen,\n\ndas Review wird auf Donnerstag verschoben.\n\nViele Grüße", "beautify", 1.5),
                ("einkaufsliste milch eier brot und kaffee", "Einkaufsliste: Milch, Eier, Brot und Kaffee.", "polish", 5),
                ("kurze zusammenfassung vom call kunde will angebot bis freitag", "Kunde wünscht Angebot bis Freitag.", "summarize", 26),
            ]
            :
            [
                ("um so i'll be a bit late tomorrow because the train is cancelled", "I'll be a bit late tomorrow because the train is cancelled.", "polish", 0.2),
                ("please tell the team the review moves to thursday", "Hi all,\n\nthe review moves to Thursday.\n\nBest regards", "beautify", 1.5),
                ("shopping list milk eggs bread and coffee", "Shopping list: milk, eggs, bread and coffee.", "polish", 5),
                ("quick summary of the call customer wants an offer by friday", "Customer wants an offer by Friday.", "summarize", 26),
            ];
        foreach (var (raw, text, stage, hours) in items)
        {
            var at = now.AddHours(-hours);
            await f.History.AddAsync(new HistoryEntry(FileHistoryStore.NewId(at), at, lang, 6200, raw, text,
                new Refinement(RefineModes.FromKey(stage)), "OpenAI · whisper-1", stage == "off" ? null : "gpt-5-mini"), CancellationToken.None);
        }
        var actions = new HistoryActions(_ => Task.FromResult(true), _ => Task.FromResult("Pasted"),
            (id, stage) => f.Controller.ReprocessVersionAsync(id, stage), f.Controller.EditHistoryAsync,
            [.. RefineModes.SettingsFor(true).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()]))]);
        var vm = new HistoryViewModel(f.History, actions, () => now);
        await vm.LoadAsync();
        var window = new HistoryWindow(vm) { Width = 900, Height = 560, RequestedThemeVariant = theme };
        window.Show();
        vm.Selected = vm.Rows[1];
        Dispatcher.UIThread.RunJobs();
        return new HelpScene(window, (Control)window.Content!, f);
    }
}
