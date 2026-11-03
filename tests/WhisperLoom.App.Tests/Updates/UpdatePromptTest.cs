using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using WhisperLoom.App.Localization;
using WhisperLoom.App.Updates;

namespace WhisperLoom.App.Tests.Updates;

/// <summary>Nachfrage vor dem Update: nur „Installieren“ bestätigt, Schließen und „Später“ nicht.</summary>
public sealed class UpdatePromptTest
{
    private static Button ButtonOf(Window window, string name) =>
        window.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == name);

    [AvaloniaFact]
    public async Task InstallierenBestaetigt()
    {
        Loc.Instance.SetLanguage("de");
        var answer = new TaskCompletionSource<bool>();
        var window = UpdatePrompt.Create("0.9.1", answer);
        window.Show();
        Assert.Contains("WhisperLoom 0.9.1 ist da", UiTest.TextsOf(window)); // UX-3: Titel nennt die Lage
        Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "Install" && b.Classes.Contains("accent"));
        Assert.Contains(window.GetLogicalDescendants().OfType<Button>(), b => b.Name == "Later" && b.Classes.Contains("text"));
        ButtonOf(window, "Install").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await answer.Task);
    }

    [AvaloniaFact]
    public async Task SpaeterUndSchliessenLehnenAb()
    {
        Loc.Instance.SetLanguage("en");
        var later = new TaskCompletionSource<bool>();
        var window = UpdatePrompt.Create("0.9.1", later);
        window.Show();
        ButtonOf(window, "Later").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(await later.Task);

        var closed = new TaskCompletionSource<bool>();
        var other = UpdatePrompt.Create("0.9.1", closed);
        other.Show();
        other.Close();
        Assert.False(await closed.Task);
    }
}
