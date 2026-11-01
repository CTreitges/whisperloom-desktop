using WhisperLoom.App.Secrets;

namespace WhisperLoom.App.Output;

/// <summary>
/// Linux-Hinweis als Desktop-Benachrichtigung über <c>notify-send</c> (freedesktop Notifications). Best effort: fehlt das
/// Programm oder antwortet es nicht, geschieht nichts – der Text liegt trotzdem in der Zwischenablage.
/// </summary>
// Doku: specifications.freedesktop.org/notification-spec/latest/ (abgerufen 2026-11-01).
internal static class DesktopNotify
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    public static async Task<bool> ShowAsync(string message, IProcessRunner? runner = null)
    {
        try
        {
            return await (runner ?? ProcessRunner.Instance).RunUntilExitAsync(
                "notify-send", ["--app-name=WhisperLoom", "--expire-time=3000", "WhisperLoom", message], "", Timeout, CancellationToken.None)
                .ConfigureAwait(false) == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or TimeoutException or IOException)
        {
            return false;
        }
    }
}
