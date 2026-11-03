using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Bühne (UX-3, Entscheidungen 12 und 19): folgt dem System, „Dunkel für Beamer“ (Strg+D, gespeichert), Hochkontrast gewinnt,
/// Pause nur während der Aufnahme, Ergebnis mit KI-Etikett, Abmelden von Sprache und Controller beim Schließen.
/// </summary>
public sealed class StageWindowTest
{
    [AvaloniaFact]
    public void Geschlossene_Buehne_haengt_nicht_mehr_an_Sprache_und_Controller()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm);
        window.Show();
        var raised = 0;
        vm.PropertyChanged += (_, _) => raised++;
        Loc.Instance.SetLanguage("en");
        Assert.True(raised > 0);

        window.Close();
        Dispatcher.UIThread.RunJobs();
        raised = 0;
        Loc.Instance.SetLanguage("de");
        f.Controller.LastResult = "neu";
        Assert.Equal(0, raised); // sonst halten Loc.Instance und der Controller Fenster und Bühne im Speicher
    }

    [AvaloniaFact]
    public void Folgt_dem_System_und_Strg_D_macht_dunkel_und_wird_gespeichert()
    {
        using var f = new ControllerFixture();
        var system = new FakeTheme();
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm, system);
        window.Show();
        Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);

        window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d");
        Assert.True(vm.DarkForProjector);
        Assert.Equal(ThemeVariant.Dark, window.RequestedThemeVariant);
        Assert.True(Services.SettingsStore.Desktop(f.Store.Load()).StageDark);
        Assert.True(window.FindControl<ToggleSwitch>("DarkSwitch")!.IsChecked);

        system.Switch(PlatformThemeVariant.Light, ColorContrastPreference.High);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(SystemTheme.HighContrast, window.RequestedThemeVariant); // Kontrastdesign gewinnt

        system.Switch(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference);
        Dispatcher.UIThread.RunJobs();
        window.KeyPress(Key.D, RawInputModifiers.Control, PhysicalKey.D, "d");
        Assert.Equal(ThemeVariant.Light, window.RequestedThemeVariant);
        Assert.False(Services.SettingsStore.Desktop(f.Store.Load()).StageDark);
        window.Close();

        // Neu geöffnet: der gespeicherte Schalter gilt.
        var again = new StageViewModel(f.Controller);
        again.ToggleDark();
        var second = new StageWindow(new StageViewModel(f.Controller), new FakeTheme());
        second.Show();
        Assert.Equal(ThemeVariant.Dark, second.RequestedThemeVariant);
        second.Close();
        again.Dispose();
    }

    [Fact]
    public void Variante_mit_erzwungenem_Dunkel()
    {
        Assert.Equal(ThemeVariant.Dark, SystemTheme.VariantOf(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, forceDark: true));
        Assert.Equal(ThemeVariant.Light, SystemTheme.VariantOf(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, forceDark: false));
        Assert.Equal(ThemeVariant.Dark, SystemTheme.VariantOf(PlatformThemeVariant.Dark, ColorContrastPreference.NoPreference, forceDark: false));
        Assert.Equal(SystemTheme.HighContrast, SystemTheme.VariantOf(PlatformThemeVariant.Light, ColorContrastPreference.High, forceDark: true));
    }

    [AvaloniaFact]
    public void Pause_nur_bei_Aufnahme_und_Ergebnis_mit_KI_Etikett()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture(new WhisperLoomSettings { Refine = new RefineSettings { Stage = "polish" } });
        var vm = new StageViewModel(f.Controller);
        var window = new StageWindow(vm, new FakeTheme());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(window.FindControl<Button>("PauseButton")!.IsVisible);
        Assert.Equal("Stufe: Glätten", vm.StageChipText);
        Assert.Equal("Glätten · KI", vm.ResultBadge);

        f.Controller.Start();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.FindControl<Button>("PauseButton")!.IsVisible);
        Assert.Contains("recording", window.FindControl<LoomIcon>("RecordDot")!.Classes);

        f.Controller.SetStage("off");
        Assert.Equal("", vm.ResultBadge); // Stufe Aus: keine KI-Ausgabe
        Assert.False(vm.HasResultBadge);
        window.Close();
    }

    private sealed class FakeTheme : IThemeSource
    {
        public event Action? Changed;

        public PlatformThemeVariant Current { get; private set; } = PlatformThemeVariant.Light;

        public ColorContrastPreference Contrast { get; private set; } = ColorContrastPreference.NoPreference;

        public void Switch(PlatformThemeVariant variant, ColorContrastPreference contrast)
        {
            (Current, Contrast) = (variant, contrast);
            Changed?.Invoke();
        }
    }
}
