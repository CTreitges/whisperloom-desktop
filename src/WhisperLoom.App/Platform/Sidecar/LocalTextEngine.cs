using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Local;

namespace WhisperLoom.App.Platform.Sidecar;

/// <summary>
/// Lokale Textverbesserung nach <c>local.textEngine</c>, je Aufruf frisch: <c>ollama</c> (Standard, bisheriges Verhalten),
/// <c>bundled</c> (mitgeliefertes Modell über <c>llama-server</c>) oder <c>server</c> (eigener OpenAI-kompatibler Server aus
/// <c>local.servers</c>, gewählt über <c>local.textServer</c>, "" = erster Text-Server).
/// </summary>
public sealed class LocalTextEngine(OllamaLocalRefiner ollama, BundledTextEngine bundled, LocalServerRefiner server, Func<WhisperLoomSettings> settings)
    : ILocalRefiner
{
    public const string Ollama = "ollama";
    public const string Bundled = "bundled";
    public const string Server = "server";

    /// <summary>Der Text-Server nach <c>local.textServer</c> ("" = erster mit Art „text“); null = keiner.</summary>
    public static LocalServer? TextServer(LocalSettings local) =>
        local.Servers.Where(s => s.Kind == "text").FirstOrDefault(s => local.TextServer.Length == 0 || s.Name == local.TextServer);

    public string Engine => LocalSettings.Of(settings()).TextEngine;

    public ILocalRefiner Current => Engine switch
    {
        Bundled => bundled,
        Server => server,
        _ => ollama,
    };

    public string ModelLabel => Current.ModelLabel;

    public Task<bool> IsReadyAsync(CancellationToken ct) => Current.IsReadyAsync(ct);

    public Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct) =>
        Current.RefineAsync(raw, language, refinement, ct);

    /// <summary>Vorladen während der Aufnahme: nur das mitgelieferte Modell braucht einen Start.</summary>
    public Task WarmUpAsync(CancellationToken ct) => Engine == Bundled ? bundled.WarmUpAsync(ct) : Task.CompletedTask;
}
