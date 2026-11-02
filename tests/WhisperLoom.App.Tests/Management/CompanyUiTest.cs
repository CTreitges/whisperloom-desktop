using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Management;
using WhisperLoom.App.Setup;
using WhisperLoom.App.Tests.Setup;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;

namespace WhisperLoom.App.Tests.Management;

/// <summary>Assistent mit Firmen-Pfad (Kriterium 1) und Seite „Firma“ (Kriterium 6), headless gegen den Fake-Server.</summary>
public sealed class CompanyUiTest
{
    /// <summary>Assistent samt Firmen-Anbindung; Abfragen warten, bis der Test <see cref="Poll"/> freigibt.</summary>
    private sealed class Wizard : IDisposable
    {
        public Wizard()
        {
            Rig = new ManagementRig(uiThread: true);
            Settings = new SettingsViewModel(Rig.F.Controller, Rig.Secrets, new FakeAccessChecker(), ["USB-Mikrofon"]) { Company = Rig.Service };
            Vm = new SetupViewModel(Settings, new SetupState(Rig.F.DataDir), new FakeAutostart(), null, null, true, "whisperloom toggle",
                Rig.Service, async (_, ct) => await Poll.WaitAsync(ct));
        }

        public ManagementRig Rig { get; }

        public SettingsViewModel Settings { get; }

        public SetupViewModel Vm { get; }

        public SemaphoreSlim Poll { get; } = new(0);

        public void Dispose() => Rig.Dispose();
    }

    private static SetupWindow Show(SetupViewModel vm)
    {
        var window = new SetupWindow(vm);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static T Named<T>(Control root, string name) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().Single(c => c.Name == name);

    [AvaloniaFact]
    public void Assistent_beginnt_mit_der_Frage_und_Fuer_mich_bleibt_wie_bisher()
    {
        Loc.Instance.SetLanguage("de");
        using var w = new Wizard();
        var window = Show(w.Vm);
        Assert.Equal(SetupStep.Usage, w.Vm.Current);
        Assert.Equal(Loc.Instance["Setup_Step_Usage"], Named<TextBlock>(window, "StepTitle").Text);
        Assert.True(Named<RadioButton>(window, "UsagePersonal").IsChecked);
        // UIA-Name der Auswahl = Titel (Screenreader, Mess-Filter); ohne ihn meldet UIA nur „Avalonia.Controls.StackPanel“.
        Assert.Equal(Loc.Instance["Setup_UsagePersonal"], Avalonia.Automation.AutomationProperties.GetName(Named<RadioButton>(window, "UsagePersonal")));
        Assert.Equal(Loc.Instance["Setup_UsageCompany"], Avalonia.Automation.AutomationProperties.GetName(Named<RadioButton>(window, "UsageCompany")));
        Assert.Equal([SetupStep.Usage, SetupStep.Language, SetupStep.Microphone, SetupStep.Recognition, SetupStep.Stage, SetupStep.Hotkey,
            SetupStep.Autostart, SetupStep.Done], w.Vm.Steps);
        w.Vm.Next();
        Assert.Equal(SetupStep.Language, w.Vm.Current);
        Assert.Empty(w.Rig.Server.Requests);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Firmen_Pfad_wartet_auf_Freigabe_und_laeuft_danach_selbst_weiter()
    {
        Loc.Instance.SetLanguage("de");
        using var w = new Wizard();
        var server = w.Rig.Server;
        server.Settings["language"] = "de";
        server.Settings["engine"] = "online";
        server.Settings["refine"] = new JsonObject { ["stage"] = "polish" };
        server.Settings["desktop"] = new JsonObject { ["uiLanguage"] = "de" };
        foreach (var p in new[] { "language", "engine", "refine.stage", "desktop.uiLanguage" })
        {
            server.Levels[p] = "fixed";
        }
        var window = Show(w.Vm);
        Named<RadioButton>(window, "UsageCompany").IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SetupStep.Company, w.Vm.Steps[1]);
        w.Vm.Next();
        Assert.Equal(SetupStep.Company, w.Vm.Current);
        Assert.False(w.Vm.NextCommand.CanExecute(null)); // erst nach Freigabe weiter

        var join = w.Vm.Join!;
        join.Server = "http://wl.example.test";
        join.Code = "abcd efgh jkmn pqrs";
        await join.JoinCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Company_ServerHttps"], join.Status); // http ins Internet abgelehnt
        Assert.Empty(server.Requests);

        join.Server = FakeManagementServer.Url;
        await join.JoinCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(JoinPhase.Pending, join.Phase);
        Assert.True(Named<StackPanel>(window, "CompanyWaiting").IsVisible);
        Assert.False(Named<StackPanel>(window, "CompanyInput").IsVisible);

        w.Poll.Release(); // erste Abfrage: noch nicht freigegeben
        await WaitUntil(() => server.Requests.Count(q => q.Path == "/api/v1/device") >= 1);
        Assert.Equal(JoinPhase.Pending, join.Phase);

        server.Set("approved");
        w.Poll.Release();
        await join.Polling;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(JoinPhase.Approved, join.Phase);
        // Fest gesetzte Schritte entfallen; Mikrofon, Hotkey-Test und Autostart bleiben; es geht von selbst weiter.
        Assert.Equal([SetupStep.Usage, SetupStep.Company, SetupStep.Microphone, SetupStep.Hotkey, SetupStep.Autostart, SetupStep.Done], w.Vm.Steps);
        Assert.Equal(SetupStep.Microphone, w.Vm.Current);
        Assert.True(w.Vm.Settings.HasPolicy);
        Assert.True(Named<WhisperLoom.App.Design.HintRow>(window, "PolicyHint").IsVisible);
        Assert.Equal("approved", w.Rig.Settings.Management.State);
        window.Close();
    }

    [AvaloniaFact]
    public void Einladungslink_belegt_den_Firmen_Schritt_vor()
    {
        using var w = new Wizard();
        w.Vm.OpenJoinLink(new JoinLink(FakeManagementServer.Url, FakeManagementServer.Code));
        Assert.Equal(SetupStep.Company, w.Vm.Current);
        Assert.True(w.Vm.UseCompany);
        Assert.Equal((FakeManagementServer.Url, FakeManagementServer.Code), (w.Vm.Join!.Server, w.Vm.Join.Code));

        // Ein ins Adressfeld eingefügter Link verteilt sich auf beide Felder.
        w.Vm.Join.Server = "whisperloom://join?server=https%3A%2F%2Fwl.other.test&code=WXYZ-WXYZ-WXYZ-WXYZ";
        Assert.Equal(("https://wl.other.test", "WXYZ-WXYZ-WXYZ-WXYZ"), (w.Vm.Join.Server, w.Vm.Join.Code));
    }

    [AvaloniaFact]
    public async Task Seite_Firma_zeigt_Verbindung_Abgleich_und_Verlassen_nach_Richtlinie()
    {
        Loc.Instance.SetLanguage("de");
        using var r = new ManagementRig(uiThread: true);
        var session = new SettingsViewModel(r.F.Controller, r.Secrets, new FakeAccessChecker()) { Company = r.Service };
        using var page = new CompanyPageViewModel(session);
        Assert.True(page.IsNotConnected);
        Assert.False(page.ShowJoin);
        Assert.True(page.ShowConnect);
        page.OpenJoinCommand.Execute(null);
        Assert.True(page.ShowJoin);
        Assert.False(page.ShowConnect); // keine leere Gruppe über dem Formular (HIG-Review)
        page.CloseJoinCommand.Execute(null);
        Assert.True(page.ShowConnect);
        page.OpenJoinCommand.Execute(null);

        r.Server.AutoApprove = true;
        r.Server.AllowLeave = false;
        page.Join!.Server = FakeManagementServer.Url;
        page.Join.Code = FakeManagementServer.Code;
        await page.Join.JoinCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(page.IsConnected);
        Assert.Equal(("Muster GmbH", FakeManagementServer.Url, Loc.Instance["Company_State_approved"]), (page.TenantName, page.ServerUrl, page.StateText));
        Assert.NotEqual(Loc.Instance["Company_LastSyncNever"], page.LastSyncText);
        Assert.False(page.CanLeave); // Firma setzt management.allowLeave Fest auf false

        await page.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Company_SyncOk"], page.Status);

        r.Server.AllowLeave = true;
        await page.SyncNowCommand.ExecuteAsync(null);
        Assert.True(page.CanLeave);
        page.LeaveCommand.Execute(null);
        Assert.True(page.LeavePending); // Rückfrage vor dem Verlassen
        await page.ConfirmLeaveCommand.ExecuteAsync(null);
        Assert.True(page.IsNotConnected);
        Assert.Equal(Loc.Instance["Company_LeftOk"], page.Status);

        // Gesperrt: Hinweis auf der Seite.
        await r.JoinAsync();
        r.Server.Set("blocked");
        await page.SyncNowCommand.ExecuteAsync(null);
        Assert.Equal(Loc.Instance["Company_State_blocked"], page.StateText);
        Assert.Equal(Loc.Instance.Format("Company_Notice_blocked", "Muster GmbH"), page.Notice);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition());
    }
}
