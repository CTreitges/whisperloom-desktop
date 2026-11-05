using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Tests.Ux;
using WhisperLoom.App.ViewModels;
using WhisperLoom.App.Views;

namespace WhisperLoom.App.Tests;

/// <summary>
/// Beschrifteter Knopf „Einstellungen“ im Bühnenkopf (Goal 2026-11-05-wld-tray-hinweis): Name für Screenreader, per Tab und Enter
/// bedienbar, ruft das Öffnen der Einstellungen auf (dieselbe Logik wie das Tray-Menü), ohne Aufruf ausgeblendet, 200 % Textgröße.
/// </summary>
public sealed class StageSettingsButtonTest
{
    private static Button SettingsButton(Window window) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "SettingsButton");

    private static bool Shown(Control c) => c.IsEffectivelyVisible && TopLevel.GetTopLevel(c) is not null;

    [AvaloniaFact]
    public void StageSettings_Knopf_hat_Namen_Beschriftung_und_oeffnet_die_Einstellungen()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var opened = 0;
        var window = new StageWindow(new StageViewModel(f.Controller, () => opened++)) { Width = 1200, Height = 700 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var button = SettingsButton(window);
        Assert.True(Shown(button));
        Assert.Equal("Einstellungen", AutomationProperties.GetName(button));
        var label = button.GetLogicalDescendants().OfType<TextBlock>().Single();
        Assert.Equal("Einstellungen", label.Text);

        Assert.Contains(button, UxFocus.TabOrder(window));
        button.Focus(NavigationMethod.Tab);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, opened);

        button.Command!.Execute(null);
        Assert.Equal(2, opened);
        window.Close();
    }

    [AvaloniaFact]
    public void StageSettings_Knopf_folgt_der_Sprache()
    {
        Loc.Instance.SetLanguage("de");
        using var f = new ControllerFixture();
        var window = new StageWindow(new StageViewModel(f.Controller, () => { }));
        window.Show();
        try
        {
            Loc.Instance.SetLanguage("en");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Settings", AutomationProperties.GetName(SettingsButton(window)));
        }
        finally
        {
            Loc.Instance.SetLanguage("de");
            window.Close();
        }
    }

    [AvaloniaFact]
    public void StageSettings_Knopf_ohne_Aufruf_ausgeblendet()
    {
        using var f = new ControllerFixture();
        var window = new StageWindow(new StageViewModel(f.Controller));
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.False(Shown(SettingsButton(window)));
        window.Close();
    }

    [AvaloniaFact]
    public void StageSettings_Knopf_bei_200_Prozent_Textgroesse_nicht_abgeschnitten()
    {
        Loc.Instance.SetLanguage("de");
        Design.TextScale.Apply(Avalonia.Application.Current!, 2.0);
        try
        {
            using var f = new ControllerFixture();
            var window = new StageWindow(new StageViewModel(f.Controller, () => { })) { Width = 1200, Height = 700 };
            window.Show();
            UxStage.Settle(window);
            var button = SettingsButton(window);
            Assert.True(Shown(button));
            var violations = UxRules.Clipped(button);
            Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
            var box = button.TranslatePoint(default, window)!.Value;
            Assert.True(box.X >= 0 && box.X + button.Bounds.Width <= window.Bounds.Width + 1, $"Knopf bei x={box.X}, Breite {button.Bounds.Width}");
            window.Close();
        }
        finally
        {
            Design.TextScale.Apply(Avalonia.Application.Current!, 1.0);
        }
    }
}
