using System.Diagnostics;

namespace WhisperLoom.App.Features.Profiles;

/// <summary>
/// „Aus laufenden Programmen wählen …“ (K2): Dateinamen der Programme mit sichtbarem Hauptfenster, wie im Task-Manager
/// („WINWORD.EXE“ erscheint als „WINWORD.exe“; der Vergleich der Profile ignoriert Groß-/Kleinschreibung). Ohne eigene App.
/// </summary>
internal static class RunningPrograms
{
    public static IReadOnlyList<string> List()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }
        var own = Environment.ProcessId;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != own && process.MainWindowHandle != IntPtr.Zero)
                    {
                        names.Add(process.ProcessName + ".exe");
                    }
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    // Prozess beendet oder ohne Zugriff: auslassen.
                }
            }
        }
        return [.. names];
    }
}
