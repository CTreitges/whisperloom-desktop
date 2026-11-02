using Avalonia.Controls;
using Avalonia.Layout;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Updates;

/// <summary>
/// Nachfrage vor dem Installieren: „Installieren und neu starten“ oder „Später“. Schließen zählt als „Später“.
/// Läuft auf dem UI-Thread.
/// </summary>
// Doku: Context7 /avaloniaui/avalonia-docs, docs/fundamentals/coded-ui.md (abgerufen 2026-10-30).
internal static class UpdatePrompt
{
    public static Task<bool> AskAsync(string version)
    {
        var answer = new TaskCompletionSource<bool>();
        Create(version, answer).Show();
        return answer.Task;
    }

    /// <summary>
    /// Rückfrage der Versionsliste (W3-CHAN): Installieren einer gewählten Version; ein Downgrade (niedrigere Version,
    /// Rückweg zu Stabil) bekommt einen eigenen Hinweis.
    /// </summary>
    public static Task<bool> AskInstallAsync(string version, bool downgrade)
    {
        var answer = new TaskCompletionSource<bool>();
        CreateInstall(version, downgrade, answer).Show();
        return answer.Task;
    }

    internal static Window CreateInstall(string version, bool downgrade, TaskCompletionSource<bool> answer) =>
        Build(Loc.Instance.Format(downgrade ? "Channel_ConfirmDowngrade" : "Channel_ConfirmInstall", version), answer);

    internal static Window Create(string version, TaskCompletionSource<bool> answer) =>
        Build(Loc.Instance.Format("Update_Available", version), answer);

    private static Window Build(string text, TaskCompletionSource<bool> answer)
    {
        var loc = Loc.Instance;
        var install = new Button { Name = "Install", Content = loc["Update_Install"], IsDefault = true };
        var later = new Button { Name = "Later", Content = loc["Update_Later"], IsCancel = true };
        var window = new Window
        {
            Title = loc["Update_Title"],
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                MaxWidth = 480,
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { later, install } },
                },
            },
        };
        install.Click += (_, _) =>
        {
            answer.TrySetResult(true);
            window.Close();
        };
        later.Click += (_, _) => window.Close();
        window.Closed += (_, _) => answer.TrySetResult(false);
        return window;
    }
}
