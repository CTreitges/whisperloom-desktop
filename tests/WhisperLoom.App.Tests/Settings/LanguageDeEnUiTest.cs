using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.ViewModels.Settings;
using WhisperLoom.App.Views;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Tests.Support;

namespace WhisperLoom.App.Tests.Settings;

/// <summary>
/// CTR-273 (W2-PFIX) headless: Sprache „Deutsch + Englisch“ auf der Seite „Erkennung“ — Auswahl nach Englisch (wie
/// <c>PrefsTest.sprachlisteMitDeutschUndEnglisch</c>), Hinweis darunter, lokal ein eigener Hinweis, gespeichert als "de+en".
/// </summary>
public sealed class LanguageDeEnUiTest
{
    private static (SettingsWindow Window, SettingsShellViewModel Shell, SettingsViewModel Vm) Open(ControllerFixture f)
    {
        var window = new SettingsWindow(new SettingsViewModel(f.Controller, new MemorySecretStore(), new FakeAccessChecker()));
        window.Show();
        return (window, window.Shell!, window.Settings!);
    }

    private static HintRow Hint(SettingsWindow window) =>
        window.GetVisualDescendants().OfType<HintRow>().Single(h => h.Name == "LanguageHintRow");

    [AvaloniaFact]
    public void Sprachliste_hat_Deutsch_und_Englisch_nach_Englisch()
    {
        Loc.Instance.SetLanguage("de");
        Assert.Equal(["auto", "de", "en", "de+en"], SettingsViewModel.RecognitionLanguages.Take(4));
        Assert.Equal("Deutsch + Englisch", SettingsViewModel.LanguageLabel("de+en"));
        Assert.Equal("DE", SettingsViewModel.LanguageLabel("de"));
        Loc.Instance.SetLanguage("en");
        Assert.Equal("German + English", SettingsViewModel.LanguageLabel("de+en"));
        Loc.Instance.SetLanguage("de");
        Assert.Equal("de", new WhisperLoomSettings().Language);
    }

    [AvaloniaFact]
    public async Task Hinweis_nur_bei_Deutsch_und_Englisch_lokal_eigener_Text_und_gespeichert()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var (window, shell, vm) = Open(f);
        SettingsUi.Show(window, "recognition");
        var page = (RecognitionPageViewModel)shell.Page("recognition");
        Assert.Null(page.LanguageHint);
        Assert.False(Hint(window).IsVisible);

        vm.Language = vm.Languages.Single(c => c.Value == WhisperLoomSettings.LanguageDeEn);
        await vm.Pending;
        Assert.Equal("de+en", f.Controller.Settings.Language);
        Assert.Equal("Deutsch + Englisch", vm.Language.Label);
        Assert.Equal("Language_deEnHint", page.LanguageHint);
        Assert.True(Hint(window).IsVisible);
        Assert.StartsWith("Für deutsche Diktate mit englischen Wörtern.", Hint(window).ShownText, StringComparison.Ordinal);

        vm.Local.Engine = vm.Local.Engines.Single(c => c.Value == "offline");
        Assert.Equal("Language_deEnOffline", page.LanguageHint);
        Assert.StartsWith("Das lokale Modell erkennt nur eine Sprache je Diktat.", Hint(window).ShownText, StringComparison.Ordinal);

        vm.Language = vm.Languages.Single(c => c.Value == "en");
        Assert.Null(page.LanguageHint);
        Assert.False(Hint(window).IsVisible);
        window.Close();
    }
}
