using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WhisperLoom.App.Views.Help;

/// <summary>Symbole der Hilfe-Seitenleiste: Schlüssel des eigenen Symbol-Sets (<c>Loom.Icon.*</c>, Design/Icons.axaml).</summary>
public static class HelpIcons
{
    /// <summary>Symbol, wenn ein Abschnitt keins nennt.</summary>
    public const string Help = "Loom.Icon.Help";

    /// <summary>Ressourcen-Schlüssel (<c>Loom.Icon.*</c>) → Geometrie; null, wenn es den Schlüssel nicht gibt.</summary>
    public static Geometry? Resolve(string icon) =>
        Application.Current is { } app && app.TryFindResource(icon, out var resource) ? resource as Geometry : null;
}
