using System.Reflection;

namespace WhisperLoom.App.Updates;

/// <summary>Version der App für <c>--version</c>: die beim Publish gesetzte <c>-p:Version</c>.</summary>
internal static class AppVersion
{
    public static string Current =>
        Format(typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Schneidet die Commit-Kennung (<c>+&lt;sha&gt;</c>) ab, die das SDK anhängt.</summary>
    internal static string Format(string? informational) =>
        string.IsNullOrEmpty(informational) ? "0.0.0" : informational.Split('+')[0];
}
