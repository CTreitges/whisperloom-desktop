using Avalonia.Controls;
using Avalonia.Layout;
using WhisperLoom.App.Design;
using WhisperLoom.App.Localization;

namespace WhisperLoom.App.Updates;

/// <summary>
/// Nachfrage vor dem Installieren (UX-3, mockups/updates.html): Titel nennt die Lage („WhisperLoom 1.1.0 ist da“), ein Satz, was
/// passiert, „Was ist neu“ als Link zu den Release-Notizen; „Jetzt installieren und neu starten“ als Hauptknopf (Enter),
/// „Später“ als Textknopf (Esc). Schließen zählt als „Später“. Folgt dem Systemthema. Läuft auf dem UI-Thread.
/// </summary>
// Doku: Context7 /avaloniaui/avalonia-docs, docs/fundamentals/coded-ui.md (abgerufen 2026-10-30).
internal static class UpdatePrompt
{
    private const string ReleaseNotes = "https://github.com/CTreitges/whisperloom-desktop/releases/tag/v";

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
        Build(version, Loc.Instance.Format("Channel_ConfirmTitle", version), Loc.Instance["Update_Body"],
            downgrade ? Loc.Instance.Format("Channel_ConfirmDowngrade", version) : null, Loc.Instance["Update_InstallChosen"], Loc.Instance["Action_Cancel"], answer);

    internal static Window Create(string version, TaskCompletionSource<bool> answer) =>
        Build(version, Loc.Instance.Format("Update_Heading", version), Loc.Instance["Update_Body"], null,
            Loc.Instance["Update_Install"], Loc.Instance["Update_Later"], answer);

    private static Window Build(string version, string title, string body, string? warning, string installText, string laterText,
        TaskCompletionSource<bool> answer)
    {
        var loc = Loc.Instance;
        var install = new Button { Name = "Install", Content = installText, IsDefault = true, Classes = { "accent" } };
        var later = new Button { Name = "Later", Content = laterText, IsCancel = true, VerticalAlignment = VerticalAlignment.Center, Classes = { "text" } };
        var notes = new Button
        {
            Name = "Notes",
            HorizontalAlignment = HorizontalAlignment.Left,
            Classes = { "text" },
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Children =
                {
                    new LoomIcon { [!LoomIcon.DataProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Loom.Icon.External") },
                    new TextBlock { Text = loc.Format("Update_Notes", version) },
                },
            },
        };
        Avalonia.Automation.AutomationProperties.SetName(notes, loc.Format("Update_Notes", version));
        var heading = new TextBlock { Name = "Heading", Text = title, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        heading[!TextBlock.FontSizeProperty] = new Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("Loom.Font.PageTitle");
        Avalonia.Automation.AutomationProperties.SetHeadingLevel(heading, 1);
        var content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 16,
            Children =
            {
                heading,
                new TextBlock { Name = "Body", Text = body, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            },
        };
        if (warning is not null)
        {
            content.Children.Add(new HintRow { Name = "DowngradeHint", Kind = HintKind.Warning, Text = warning });
        }
        content.Children.Add(notes);
        // Hauptknopf zuletzt (rechts); bei 200 % bricht die Zeile um, die Reihenfolge bleibt.
        content.Children.Add(new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 16, LineSpacing = 8, Children = { later, install } });
        var window = new Window
        {
            Title = loc["Update_Title"],
            Width = 480,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = content,
            Classes = { "loom" },
        };
        SystemTheme.Follow(window, Avalonia.Application.Current?.PlatformSettings);
        install.Click += (_, _) =>
        {
            answer.TrySetResult(true);
            window.Close();
        };
        later.Click += (_, _) => window.Close();
        notes.Click += async (_, _) => await window.Launcher.LaunchUriAsync(new Uri(ReleaseNotes + version));
        window.Closed += (_, _) => answer.TrySetResult(false);
        return window;
    }
}
