using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using WhisperLoom.App.Design;
using WhisperLoom.App.Features.History;
using WhisperLoom.App.Features.Selection;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Platform.Windows.Overlay;
using WhisperLoom.App.Platform.Windows.Pill;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Management;
using WhisperLoom.App.Tests.Setup;
using WhisperLoom.App.Tests.Updates;
using WhisperLoom.App.Updates;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Updates;
using WhisperLoom.App.Views;
using WhisperLoom.App.Views.Help;
using WhisperLoom.App.Views.Updates;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;
using WhisperLoom.Core.Settings;

namespace WhisperLoom.App.Tests.Ux;

/// <summary>Szenen der übrigen Fenster (Einrichtung, Verlauf, Hilfe, Bühne, Overlay, Pille, Hinweis, Markierung, Updates).</summary>
internal static class UxWindowScenes
{
    /// <summary>Einrichtung ab „Wie nutzt du WhisperLoom?“ (mit Firmen-Anbindung); <paramref name="company"/> = Firmen-Schritt.</summary>
    public static Task<UxScene> SetupUsage(ThemeVariant theme, bool company)
    {
        var rig = new ManagementRig(uiThread: true);
        var settings = new SettingsViewModel(rig.F.Controller, rig.Secrets, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = rig.Service };
        var vm = new SetupViewModel(settings, new SetupState(rig.F.DataDir), new FakeAutostart(), null, null, true, "whisperloom toggle", rig.Service);
        var window = new SetupWindow(vm) { RequestedThemeVariant = theme };
        window.Show();
        if (company)
        {
            vm.UseCompany = true;
            vm.Next();
            vm.Join!.Server = "https://wl.example.com";
            vm.Join.Code = "ABCD-EFGH-JKMN-PQRS";
        }
        UxStage.FitHeight(window);
        return Task.FromResult(new UxScene(window, window, rig));
    }

    /// <summary>Einrichtung ohne Firma bei Schritt <paramref name="step"/> (Windows-Bedienung, Test-Key, Mikrofon-Pegel).</summary>
    public static async Task<UxScene> SetupAt(ThemeVariant theme, SetupStep step)
    {
        var s = new SetupFixture(recordHotkey: () => Task.FromResult<string?>("Ctrl+Win"));
        s.Secrets.Values[SecretNames.Stt] = "test-key-not-real-62";
        await s.Settings.LoadKeysAsync();
        var window = new SetupWindow(s.Vm) { RequestedThemeVariant = theme };
        window.Show();
        while (s.Vm.Current != step)
        {
            s.Vm.Next();
        }
        if (step == SetupStep.Microphone)
        {
            s.Settings.Microphone = s.Settings.Microphones[1];
            s.Mic.Start();
            s.Mic.Tick();
        }
        UxStage.FitHeight(window);
        return new UxScene(window, window, s);
    }

    public static async Task<UxScene> History(ThemeVariant theme, bool empty)
    {
        var now = new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.FromHours(1));
        var f = new ControllerFixture(new WhisperLoomSettings { Language = "de", Refine = new RefineSettings { Stage = "polish" } }) { Now = now };
        (string Raw, string Text, string Stage, double Hours)[] items = empty ? [] :
        [
            ("ähm also ich komme morgen etwas später weil der zug ausfällt", "Ich komme morgen etwas später, weil der Zug ausfällt.", "polish", 0.2),
            ("schreib bitte an das team dass das review auf donnerstag verschoben wird", "Hallo zusammen,\n\ndas Review wird auf Donnerstag verschoben.\n\nViele Grüße", "beautify", 1.5),
            ("einkaufsliste milch eier brot und kaffee", "Einkaufsliste: Milch, Eier, Brot und Kaffee.", "polish", 5),
            ("kurze zusammenfassung vom call kunde will angebot bis freitag", "Kunde wünscht Angebot bis Freitag.", "summarize", 26),
        ];
        foreach (var (raw, text, stage, hours) in items)
        {
            var at = now.AddHours(-hours);
            await f.History.AddAsync(new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 6200, raw, text,
                new Refinement(RefineModes.FromKey(stage)), "OpenAI · gpt-transcribe", "gpt-6-luna"), CancellationToken.None);
        }
        var actions = new HistoryActions(_ => Task.FromResult(true), _ => Task.FromResult("Pasted"),
            (id, stage) => f.Controller.ReprocessVersionAsync(id, stage), f.Controller.EditHistoryAsync,
            [.. RefineModes.SettingsFor(true).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()]))]);
        var vm = new HistoryViewModel(f.History, actions, () => now);
        await vm.LoadAsync();
        var window = new HistoryWindow(vm) { Width = 960, Height = 640, RequestedThemeVariant = theme };
        window.Show();
        if (!empty)
        {
            vm.Selected = vm.Rows[1];
        }
        UxStage.Settle(window);
        return new UxScene(window, window, f);
    }

    public static Task<UxScene> Help(ThemeVariant theme, string? topic)
    {
        var window = new HelpWindow(new HelpViewModel()) { Width = 1000, Height = 720, RequestedThemeVariant = theme };
        window.Show();
        if (topic is not null)
        {
            window.ShowTopic(topic);
        }
        UxStage.FitHeight(window);
        return Task.FromResult(new UxScene(window, window, null));
    }

    /// <summary>Bühne (immer dunkel, Beamer): Aufnahme läuft bzw. Ergebnis da.</summary>
    public static async Task<UxScene> Stage(ThemeVariant theme, bool result)
    {
        var f = new ControllerFixture(new WhisperLoomSettings { Language = "de", Refine = new RefineSettings { Stage = "polish" } });
        f.Pipeline.RawText = "ähm also das meeting ist am donnerstag um zehn und bitte bringt die zahlen mit";
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm) { Width = 1200, Height = 700, RequestedThemeVariant = UxStage.AppFixed };
        window.Show();
        f.Controller.Start();
        f.Now += TimeSpan.FromSeconds(6);
        if (result)
        {
            await f.Controller.StopAsync();
            f.Controller.LastResult = "Das Meeting ist am Donnerstag um 10 Uhr. Bitte bringt die Zahlen mit.";
        }
        vm.Tick();
        UxStage.Settle(window);
        return new UxScene(window, window, f);
    }

    public static Task<UxScene> Overlay(ThemeVariant theme, OverlayState state, bool picker = false)
    {
        var vm = new OverlayViewModel(() => { }, () => { }, () => { })
        {
            Stage = Loc.Instance["Stage_polish"],
            Elapsed = TimeSpan.FromSeconds(14),
            Level = 0.55,
            State = state,
            ErrorKey = state == OverlayState.Error ? "Error_Network" : null,
        };
        if (picker)
        {
            vm.StagePicker = new StagePickerViewModel(RefineModes.SettingsFor(true).Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])), "polish");
            vm.IsStagePickerOpen = true;
        }
        var window = UxStage.OnBackdrop(new OverlayWindow(vm), theme);
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null));
    }

    public static Task<UxScene> Pill(ThemeVariant theme, bool processing)
    {
        var window = UxStage.OnBackdrop(new PillWindow(processing ? new PillViewModel { Processing = true } : new PillViewModel { Level = 0.6 }), theme);
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null));
    }

    public static Task<UxScene> Hint(ThemeVariant theme, bool toast)
    {
        var window = UxStage.OnBackdrop(new HintWindow(), theme);
        if (toast)
        {
            window.SetContent("toast", "WhisperLoom", Loc.Instance["Output_Hint_Copied"]);
        }
        else
        {
            window.SetContent("stage", Loc.Instance["Stage_Label"], Loc.Instance["Stage_beautify"]);
        }
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null));
    }

    /// <summary>Schwebender Knopf neben einer Markierung (Inhalt des Fensters auf dem Hintergrund der Variante).</summary>
    public static Task<UxScene> SelectionButton(ThemeVariant theme)
    {
        var window = UxStage.Hosted(new SelectionButtonWindow(), theme, 320, 120, new Thickness(24));
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null, typeof(SelectionButtonWindow)));
    }

    /// <summary>Stufen-Wähler am Cursor; Stufe 2 markiert wie nach ↓.</summary>
    public static Task<UxScene> SelectionPicker(ThemeVariant theme)
    {
        var stages = new[] { RefineMode.Polish, RefineMode.Beautify, RefineMode.Summarize }
            .Select(m => (m.Key(), Loc.Instance["Stage_" + m.Key()])).ToList();
        stages.Add(("instruction", Loc.Instance["Selection_Instruction"]));
        var picker = new StagePickerViewModel(stages, "polish");
        picker.Move(1);
        var source = new SelectionPickerWindow(noActivate: false) { DataContext = picker };
        var window = UxStage.Hosted(source, theme, 360, 320, new Thickness(24));
        ((Control)window.Content!).Width = 290;
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null, typeof(SelectionPickerWindow)));
    }

    public static async Task<UxScene> Versions(ThemeVariant theme)
    {
        var f = new ControllerFixture();
        var client = new FakeUpdateClient { CurrentVersion = "1.1.0-beta.1" };
        client.Feeds[FakeUpdateClient.Stable] = ["1.0.0", "1.0.1"];
        client.Feeds[FakeUpdateClient.Beta] = ["1.1.0-beta.1", "1.1.0-beta.2"];
        client.Feeds[FakeUpdateClient.Test] = ["1.1.0-test.7.feat-x"];
        var vm = new VersionListViewModel(client, f.Log, () => PolicySettings.None, (_, _) => Task.FromResult(false));
        await vm.LoadAsync();
        var window = new VersionListWindow(vm) { RequestedThemeVariant = theme };
        window.Show();
        UxStage.Settle(window);
        return new UxScene(window, window, f);
    }

    public static Task<UxScene> UpdatePromptScene(ThemeVariant theme)
    {
        var window = UpdatePrompt.Create("1.1.0", new TaskCompletionSource<bool>());
        window.RequestedThemeVariant = UxStage.AppFixed;
        window.Show();
        UxStage.Settle(window);
        return Task.FromResult(new UxScene(window, window, null));
    }
}
