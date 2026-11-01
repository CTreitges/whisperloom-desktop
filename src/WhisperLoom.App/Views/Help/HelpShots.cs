using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;

namespace WhisperLoom.App.Views.Help;

/// <summary>
/// Ein Screenshot der Hilfe aus <c>Assets/Help/screens.json</c>: Name, Abschnitt, Szene (die Headless-Tests bauen sie mit
/// Fake-Daten nach) und Markierungen (<c>path:&lt;Einstellung&gt;</c> = Zeile, <c>name:&lt;x:Name&gt;</c> = Steuerelement), die
/// <c>tools/gen_help_screens.py</c> als Rahmen mit Ziffer einzeichnen lässt. Texte: <c>HelpShot_&lt;Name&gt;</c> (Beschreibung und
/// Alternativtext), <c>HelpShot_&lt;Name&gt;_&lt;n&gt;</c> (Legende der Ziffer n).
/// </summary>
public sealed record HelpShot(string Name, string Section, string Scene, IReadOnlyList<string> Markers, int Height = 0)
{
    public string CaptionKey => $"HelpShot_{Name}";

    public string LegendKey(int number) => $"HelpShot_{Name}_{number}";

    /// <summary>PNG je Sprache („de“/„en“) und Variante („light“/„dark“).</summary>
    public static string AssetPath(string name, string language, string theme) =>
        $"avares://WhisperLoom/Assets/Help/screens/{language}-{theme}/{name}.png";
}

/// <summary>Liest die Screenshot-Liste der Hilfe (eingebettet).</summary>
public static class HelpShots
{
    public const string SpecPath = "avares://WhisperLoom/Assets/Help/screens.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static IReadOnlyList<HelpShot>? _cache;

    public static IReadOnlyList<HelpShot> Load() => _cache ??= Parse(ReadSpec());

    public static IReadOnlyList<HelpShot> Parse(string json) =>
        JsonSerializer.Deserialize<Spec>(json, Options)?.Screens ?? [];

    private static string ReadSpec()
    {
        using var stream = AssetLoader.Open(new Uri(SpecPath));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed record Spec([property: JsonPropertyName("screens")] IReadOnlyList<HelpShot> Screens);
}
