using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Services;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.History;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>Seite „Ausgabe“ (immer kopieren, Enter danach) und „Verlauf & Datenschutz“ (Verkleinern fragt) – W2-OUT.</summary>
public sealed class OutputSettingsTest
{
    private static (SettingsWindow Window, SettingsViewModel Vm) Open(ControllerFixture f)
    {
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        return (window, window.Settings!);
    }

    [AvaloniaFact]
    public async Task Immer_kopieren_und_Enter_stehen_auf_der_Ausgabe_Seite_und_speichern_sofort()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, vm) = Open(f);
        SettingsUi.Show(window, "output");
        if (OperatingSystem.IsWindows())
        {
            Assert.True(SettingsUi.Row(window, "output.alwaysCopy").IsVisible);
            Assert.True(SettingsUi.Row(window, "output.pressEnter").IsVisible);
            Assert.Equal(Loc.Instance["Set_PressEnter"], SettingsUi.Row(window, "output.pressEnter").Title);
        }
        Assert.False(vm.AlwaysCopy);
        Assert.False(vm.PressEnter);

        vm.AlwaysCopy = true;
        vm.PressEnter = true;
        await vm.Pending;
        Assert.True(f.Controller.Settings.Output.AlwaysCopy);
        Assert.True(f.Controller.Settings.Output.PressEnter);
        Assert.Contains("\"pressEnter\": true", File.ReadAllText(f.Store.FilePath), StringComparison.Ordinal);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Profil_Editor_zeigt_Erben_An_Aus()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, vm) = Open(f);
        Assert.Equal(3, vm.Profiles.EnterChoices.Count);
        window.Close();
        await Task.CompletedTask;
    }

    private static async Task Fill(ControllerFixture f, int n)
    {
        var t0 = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < n; i++)
        {
            var at = t0.AddMinutes(i);
            await f.History.AddAsync(new HistoryEntry(FileHistoryStore.NewId(at), at, "de", 1000, $"roh {i}", $"Text {i}", Refinement.Off, "x"),
                CancellationToken.None);
        }
    }

    [AvaloniaFact]
    public async Task Verkleinern_mit_zu_vielen_Eintraegen_fragt_und_kuerzt_erst_nach_Ja()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture(new WhisperLoomSettings { History = new HistorySettings { Size = 25 } });
        await Fill(f, 12);
        var (window, vm) = Open(f);

        vm.HistorySizeChoice = 10;
        await vm.HistorySizePending;
        Assert.True(vm.HistoryShrinkPending);
        Assert.Contains("2", vm.HistoryShrinkText, StringComparison.Ordinal);
        Assert.Equal(25, f.Controller.Settings.History.Size);

        vm.CancelHistoryShrinkCommand.Execute(null);
        Assert.False(vm.HistoryShrinkPending);
        Assert.Equal(25, vm.HistorySizeChoice);
        Assert.Equal(12, (await f.History.ListAsync(CancellationToken.None)).Count);

        vm.HistorySizeChoice = 10;
        await vm.HistorySizePending;
        await vm.ConfirmHistoryShrinkCommand.ExecuteAsync(null);
        Assert.Equal(10, f.Controller.Settings.History.Size);
        var left = await f.History.ListAsync(CancellationToken.None);
        Assert.Equal(10, left.Count);
        Assert.Equal("Text 11", left[0].Text); // die neuesten bleiben
        window.Close();
    }

    [AvaloniaFact]
    public async Task Verkleinern_ohne_Ueberhang_und_Vergroessern_fragen_nicht()
    {
        using var f = new ControllerFixture(new WhisperLoomSettings { History = new HistorySettings { Size = 50 } });
        await Fill(f, 5);
        var (window, vm) = Open(f);
        vm.HistorySizeChoice = 10;
        await vm.HistorySizePending;
        Assert.False(vm.HistoryShrinkPending);
        await vm.Pending;
        Assert.Equal(10, f.Controller.Settings.History.Size);
        vm.HistorySizeChoice = 500;
        await vm.HistorySizePending;
        await vm.Pending;
        Assert.Equal(500, f.Controller.Settings.History.Size);
        window.Close();
    }
}
