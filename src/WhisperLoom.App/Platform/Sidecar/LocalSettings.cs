using System.Text.Json;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Desktop-eigene Einstellungen der lokalen Verarbeitung im Abschnitt <c>local</c> der obersten Ebene von settings.json
/// (<see cref="WhisperLoomSettings.Extra"/>). Modell und Genauigkeit der Erkennung stehen im Vertrag (<c>offline</c>);
/// <c>offline.localLlmModel</c> bleibt die LiteRT-ID für den Sync mit Android, das Ollama-Modell steht hier.
/// </summary>
public sealed class LocalSettings
{
    public const string SectionName = "local";

    public const int DefaultIdleMinutes = 10;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Ollama-Modell der lokalen Textverbesserung ("" = keins gewählt).</summary>
    public string OllamaModel { get; set; } = "";

    /// <summary>Adresse von Ollama; "" = http://127.0.0.1:11434 (Standard, auch für <c>OLLAMA_HOST</c> mit anderem Port).</summary>
    public string OllamaUrl { get; set; } = "";

    /// <summary>"auto" = Vulkan, bei Fehler CPU; "cpu" = nur CPU.</summary>
    public string Backend { get; set; } = "auto";

    /// <summary>Der Begleitprozess endet nach so vielen Minuten ohne Erkennung.</summary>
    public int IdleMinutes { get; set; } = DefaultIdleMinutes;

    /// <summary>Lokale Textverbesserung (W2): "ollama" (Standard, bisheriges Verhalten), "bundled" (mitgeliefertes Modell) oder "server".</summary>
    public string TextEngine { get; set; } = "ollama";

    /// <summary>ID des mitgelieferten Textmodells bei <see cref="TextEngine"/> "bundled"; "" = Empfehlung.</summary>
    public string BundledModel { get; set; } = "";

    /// <summary>Lokale OpenAI-kompatible Server für Erkennung bzw. Text (W2); gerätebezogen: Import und Export lassen sie weg.</summary>
    public List<LocalServer> Servers { get; set; } = [];

    public static LocalSettings Of(WhisperLoomSettings settings)
    {
        if (settings.Extra is not null && settings.Extra.TryGetValue(SectionName, out var element))
        {
            try
            {
                return element.Deserialize<LocalSettings>(Json) ?? new LocalSettings();
            }
            catch (JsonException)
            {
                return new LocalSettings();
            }
        }
        return new LocalSettings();
    }

    public static void Set(WhisperLoomSettings settings, LocalSettings local)
    {
        settings.Extra ??= [];
        settings.Extra[SectionName] = JsonSerializer.SerializeToElement(local, Json);
    }
}
