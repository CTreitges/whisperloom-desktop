using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Threading;

namespace WhisperLoom.App.Design;

/// <summary>
/// Rückmeldung „Kopiert“ an einem Kopier-Knopf: zwei Texte im Knopf, beide an die Sprache gebunden; „Kopiert“ steht
/// <see cref="Duration"/> lang da, danach wieder „Kopieren“ (Prüfer-Fund PR #42: früher dauerhaft überschrieben).
/// Ein neuer Klick startet die Zeit neu (ein Timer je Knopf).
/// </summary>
internal static class CopyFeedback
{
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(2);

    private static readonly ConditionalWeakTable<Control, DispatcherTimer> Timers = [];

    public static void Show(Control idle, Control done)
    {
        Set(idle, done, copied: true);
        if (Timers.TryGetValue(done, out var running))
        {
            running.Stop();
        }
        var timer = new DispatcherTimer { Interval = Duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Timers.Remove(done);
            Set(idle, done, copied: false);
        };
        Timers.AddOrUpdate(done, timer);
        timer.Start();
    }

    private static void Set(Control idle, Control done, bool copied)
    {
        idle.IsVisible = !copied;
        done.IsVisible = copied;
    }
}
