using Avalonia.Headless.XUnit;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Setup;

namespace WhisperLoom.App.Tests.Setup;

/// <summary>
/// TP-WSETUP (Fund 2026-11-04): Ein Run-Eintrag, der nach der Installation noch auf den Dev-Build zeigte, blieb stehen –
/// <see cref="IAutostart.IsEnabled"/> prüft nur die Existenz. Reparatur beim Start, Fertig im Assistenten und die
/// Deinstallation gegen <see cref="FakeAutostart"/> (nie gegen den echten Run-Wert).
/// </summary>
public sealed class AutostartRepairTest
{
    private const string DevBuild = @"C:\Users\Test\Documents\whisperloom-desktop\src\WhisperLoom.App\bin\Debug\net10.0\WhisperLoom.exe";

    [Fact]
    public void Repair_schreibt_einen_fremden_Pfad_auf_die_eigene_Exe_um()
    {
        var autostart = new FakeAutostart { Target = DevBuild };

        Assert.Equal("repaired", Autostart.Repair(autostart, installed: true));

        Assert.Equal(FakeAutostart.Self, autostart.Target);
        Assert.Equal(1, autostart.Writes);
    }

    [Fact]
    public void Repair_laesst_den_eigenen_Eintrag_unveraendert()
    {
        var autostart = new FakeAutostart { Target = FakeAutostart.Self };

        Assert.Equal("unchanged", Autostart.Repair(autostart, installed: true));

        Assert.Equal(FakeAutostart.Self, autostart.Target);
        Assert.Equal(0, autostart.Writes);
    }

    [Fact]
    public void Repair_legt_keinen_fehlenden_Eintrag_an()
    {
        var autostart = new FakeAutostart();

        Assert.Equal("unchanged", Autostart.Repair(autostart, installed: true));

        Assert.Null(autostart.Target);
        Assert.Equal(0, autostart.Writes);
    }

    [Fact]
    public void Repair_holt_den_Eintrag_nicht_zu_einem_nicht_installierten_Build()
    {
        var autostart = new FakeAutostart(self: DevBuild) { Target = FakeAutostart.Self };

        Assert.Equal("unchanged", Autostart.Repair(autostart, installed: false));

        Assert.Equal(FakeAutostart.Self, autostart.Target);
    }

    [Fact]
    public void Repair_meldet_einen_Schreibfehler_ohne_Ausnahme()
    {
        var autostart = new FakeAutostart { Target = DevBuild, FailWrites = true };

        Assert.Equal("failed", Autostart.Repair(autostart, installed: true));
        Assert.Equal("unchanged", Autostart.Repair(null, installed: true));
    }

    [Fact]
    public void Uninstall_entfernt_den_Eintrag()
    {
        var autostart = new FakeAutostart { Target = FakeAutostart.Self };

        Autostart.RemoveOnUninstall(autostart);

        Assert.Null(autostart.Target);
        Assert.False(autostart.IsEnabled);
        Autostart.RemoveOnUninstall(autostart); // fehlender Eintrag: kein Fehler
        Autostart.RemoveOnUninstall(null);
    }

    [Fact]
    public void Uninstall_laeuft_bei_einem_Fehler_weiter()
    {
        var autostart = new FakeAutostart { Target = FakeAutostart.Self, FailWrites = true };

        Autostart.RemoveOnUninstall(autostart); // wirft nicht

        Assert.Equal(FakeAutostart.Self, autostart.Target);
    }

    /// <summary>SetupViewModel.Finish schrieb nur, wenn der Schalter von IsEnabled abwich: ein fremder Pfad blieb.</summary>
    [AvaloniaFact]
    public async Task Fertig_im_Assistenten_korrigiert_einen_fremden_Pfad()
    {
        Loc.Instance.SetLanguage("de");
        var autostart = new FakeAutostart { Target = DevBuild };
        using var s = new SetupFixture(autostart: autostart);
        Assert.True(s.Vm.AutostartEnabled); // Schalter steht an, weil ein Eintrag existiert

        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        await s.Vm.FinishCommand.ExecuteAsync(null);

        Assert.Equal("", s.Vm.Status);
        Assert.Equal(FakeAutostart.Self, autostart.Target);
    }

    [AvaloniaFact]
    public async Task Fertig_mit_ausgeschaltetem_Schalter_entfernt_auch_einen_fremden_Eintrag()
    {
        Loc.Instance.SetLanguage("de");
        var autostart = new FakeAutostart { Target = DevBuild };
        using var s = new SetupFixture(autostart: autostart);
        s.Vm.AutostartEnabled = false;

        while (s.Vm.Current != SetupStep.Done)
        {
            s.Vm.Next();
        }
        await s.Vm.FinishCommand.ExecuteAsync(null);

        Assert.Null(autostart.Target);
    }
}
