using Avalonia.Controls;
using Avalonia.Threading;

namespace WhisperLoom.App.Design;

/// <summary>
/// Rückmeldung „Kopiert“ an einem Kopier-Knopf: zwei Texte im Knopf, beide an die Sprache gebunden; „Kopiert“ steht
/// <see cref="Duration"/> lang da, danach wieder „Kopieren“ (Prüfer-Fund PR #42: früher dauerhaft überschrieben).
/// </summary>
internal static class CopyFeedback
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    public static void Show(Control idle, Control done)
    {
        Set(idle, done, copied: true);
        DispatcherTimer.RunOnce(() => Set(idle, done, copied: false), Duration);
    }

    private static void Set(Control idle, Control done, bool copied)
    {
        idle.IsVisible = !copied;
        done.IsVisible = copied;
    }
}
