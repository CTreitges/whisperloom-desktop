namespace WhisperLoom.App.Platform;

/// <summary>Registriert das URL-Schema <c>whisperloom://</c> für Einladungslinks je OS (W2-ENR); ein Fehler stoppt den Start nie.</summary>
public static class JoinLinkRegistration
{
    /// <returns>Kurzer Status fürs Log: <c>ok</c>, <c>unchanged</c>, <c>file-only</c>, <c>error</c>, <c>unsupported</c>.</returns>
    public static string Register(string exePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var scheme = new Windows.JoinLinkScheme(Windows.JoinLinkScheme.ClassesKey, exePath);
                if (scheme.IsRegistered)
                {
                    return "unchanged";
                }
                scheme.Register();
                return "ok";
            }
            if (OperatingSystem.IsLinux())
            {
                var scheme = new Linux.JoinLinkScheme(Linux.JoinLinkScheme.DefaultDirectory(), exePath);
                if (File.Exists(scheme.FilePath) && File.ReadAllText(scheme.FilePath) == scheme.Content())
                {
                    return "unchanged";
                }
                return scheme.Register() ? "ok" : "file-only";
            }
            return "unsupported";
        }
        catch (IOException)
        {
            return "error";
        }
    }
}
