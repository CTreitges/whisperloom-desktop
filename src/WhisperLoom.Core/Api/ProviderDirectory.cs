using System.Text.Json;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Api;

/// <summary>
/// Eine Quelle von Anbietern außerhalb des Katalogs mit eigenem Präfix der ID (<c>custom:</c>, <c>local:</c>; W2-ENR
/// ergänzt z. B. <c>company:</c>). Jeder gelieferte Anbieter hat eine feste Adresse (<see cref="ProviderInfo.Family"/>
/// gesetzt): Ein Key geht nur an den Host des Eintrags, nie an eine Adresse aus dem Zugang.
/// </summary>
public interface IProviderSource
{
    /// <summary>Präfix der IDs, z. B. <c>"custom:"</c>.</summary>
    string Prefix { get; }

    /// <summary>Die Anbieter mit vollständiger ID (<see cref="Prefix"/> + Name).</summary>
    IReadOnlyList<ProviderInfo> Providers { get; }
}

/// <summary>
/// Löst eine Anbieter-ID eines Zugangs auf (<c>*.providerId</c>): Katalog (<see cref="ProviderCatalog"/>), eigene
/// Anbieter (<c>providers.custom</c>) und lokale Server (<c>local.servers</c>). Eine unbekannte ID mit Präfix wird nie
/// zu OpenAI (dorthin ginge sonst der Key des eigenen Servers), sondern zu einem Anbieter ohne Adresse (unvollständig).
/// </summary>
public sealed class ProviderDirectory(IReadOnlyList<IProviderSource> sources, IReadOnlyDictionary<string, string>? keys = null)
{
    /// <summary>Nur der Katalog (Verhalten vor W2).</summary>
    public static ProviderDirectory Catalog { get; } = new([]);

    public IReadOnlyList<IProviderSource> Sources { get; } = sources;

    /// <summary>Katalog und Einträge in Anzeige-Reihenfolge.</summary>
    public IEnumerable<ProviderInfo> All => ProviderCatalog.Providers.Concat(Sources.SelectMany(s => s.Providers));

    /// <summary>Verzeichnis aus den Einstellungen: eigene Anbieter, lokale Server und (W2-ENR) der Proxy der Firma.</summary>
    public static ProviderDirectory For(WhisperLoomSettings s) =>
        new([new CustomProviderSource(s.Providers.Custom), new LocalServerSource(LocalServerSource.Read(s)), new CompanyProviderSource(s.Management)]);

    /// <summary>Das Verzeichnis mit einer weiteren Quelle (z. B. Firmen-Anbieter).</summary>
    public ProviderDirectory With(IProviderSource source) => new([.. Sources, source], _keys);

    private readonly IReadOnlyDictionary<string, string> _keys = keys ?? new Dictionary<string, string>();

    /// <summary>Dasselbe Verzeichnis mit den Keys der Einträge (Name = Anbieter-ID, <see cref="SecretNames.ForProvider"/>).</summary>
    public ProviderDirectory WithKeys(IReadOnlyDictionary<string, string> keys) => new(Sources, keys);

    /// <summary>
    /// Key eines Eintrags: nur sein eigener (nie der des Zugangs, nie geerbt); lokale Server haben keinen. "" = keiner.
    /// </summary>
    public string KeyOf(ProviderInfo entry) => _keys.GetValueOrDefault(entry.Id, "").Trim();

    public ProviderInfo? Find(string id) =>
        ProviderCatalog.Find(id) ?? Sources.Where(s => id.StartsWith(s.Prefix, StringComparison.Ordinal))
            .SelectMany(s => s.Providers).FirstOrDefault(p => p.Id == id);

    /// <summary>Unbekannte ID ohne Präfix bzw. leer → OpenAI (wie Android); mit Präfix → <see cref="Missing"/>.</summary>
    public ProviderInfo ById(string id) =>
        Find(id) ?? (id.Contains(':', StringComparison.Ordinal) ? Missing(id) : ProviderCatalog.OpenAi);

    /// <summary>Gelöschter oder fehlender Eintrag: ohne Adresse, also nie vollständig.</summary>
    public static ProviderInfo Missing(string id) =>
        new(id, id, "", NeedsKey: true, AllowsHttp: true, SttModels: [], LlmModels: [], Family: ProviderCatalog.CustomId);
}

/// <summary>Eigene Anbieter <c>custom:&lt;id&gt;</c> aus <c>providers.custom</c>.</summary>
public sealed class CustomProviderSource(IEnumerable<CustomProvider> entries) : IProviderSource
{
    /// <summary>
    /// Eine gemischte Liste (Erkennung + Text) nach Namen trennen, damit die Empfehlung der Text-Stufe nie ein
    /// Erkennungsmodell ist (HIG-Review W2-LOCAL Nr. 3). Passt keins zur Art, gilt die ganze Liste. null = nicht trennen.
    /// </summary>
    private static List<ModelOption> Split(List<ModelOption> models, bool? stt)
    {
        if (stt is not { } wantStt) return models;
        var part = models.Where(m => ModelListFilters.LooksLikeStt(m.Id) == wantStt).ToList();
        return part.Count > 0 ? part : models;
    }

    public string Prefix => ProvidersSettings.CustomPrefix;

    public IReadOnlyList<ProviderInfo> Providers { get; } = [.. entries.Select(ToProvider)];

    /// <summary>
    /// Protokoll → Regeln: openai = eigener Server (Erkennung + Text), anthropic = Text über die OpenAI-Schicht,
    /// ollama = native API (Text), elevenlabs = Erkennung. Die Modell-IDs des Eintrags gelten für jede Fähigkeit.
    /// </summary>
    public static ProviderInfo ToProvider(CustomProvider c)
    {
        List<ModelOption> models = [.. (c.Models ?? []).Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => new ModelOption(m.Trim(), m.Trim()))];
        var (family, api) = c.Protocol switch
        {
            "anthropic" => ("anthropic", ApiStyle.OpenAi),
            "ollama" => (ProviderCatalog.OllamaId, ApiStyle.Ollama),
            "elevenlabs" => (ProviderCatalog.ElevenLabsId, ApiStyle.ElevenLabs),
            _ => (ProviderCatalog.CustomId, ApiStyle.OpenAi),
        };
        var catalog = ProviderCatalog.Find(family);
        return new ProviderInfo(
            Id: ProvidersSettings.CustomPrefix + c.Id,
            Name: string.IsNullOrWhiteSpace(c.Name) ? c.Id : c.Name.Trim(),
            BaseUrl: (c.BaseUrl ?? "").Trim(),
            NeedsKey: !c.KeyOptional,
            AllowsHttp: true,
            DefaultReadTimeoutSec: catalog?.DefaultReadTimeoutSec ?? 90,
            SttModels: c.HasStt ? Split(models, stt: c.HasLlm ? true : null) : null,
            LlmModels: c.HasLlm ? Split(models, stt: c.HasStt ? false : null) : null,
            SttSendsPrompt: catalog?.SttSendsPrompt ?? true,
            SttSendsResponseFormat: catalog?.SttSendsResponseFormat ?? true,
            SttPathOverride: null,
            SttMaxBytes: null,
            Api: api,
            Family: family);
    }
}

/// <summary>
/// Lokale OpenAI-kompatible Server <c>local:&lt;name&gt;</c> aus <c>local.servers</c> (LM Studio, llama.cpp,
/// faster-whisper-server, Ollama über <c>/v1</c>): ohne Key, Art „stt“ nur Erkennung, „text“ nur Textverbesserung.
/// </summary>
public sealed class LocalServerSource(IEnumerable<LocalServer> servers) : IProviderSource
{
    public const string LocalPrefix = "local:";

    public string Prefix => LocalPrefix;

    public IReadOnlyList<ProviderInfo> Providers { get; } = [.. servers.Select(ToProvider)];

    public static ProviderInfo ToProvider(LocalServer s)
    {
        List<ModelOption> models = string.IsNullOrWhiteSpace(s.Model) ? [] : [new ModelOption(s.Model.Trim(), s.Model.Trim())];
        var stt = s.Kind == "stt";
        return new ProviderInfo(
            Id: LocalPrefix + s.Name,
            Name: s.Name,
            BaseUrl: (s.BaseUrl ?? "").Trim(),
            NeedsKey: false,
            AllowsHttp: true,
            DefaultReadTimeoutSec: 600,
            SttModels: stt ? models : null,
            LlmModels: stt ? null : models,
            Family: ProviderCatalog.CustomId);
    }

    /// <summary>
    /// <c>local.servers</c> aus dem App-Abschnitt <c>local</c> (im Vertrag nur als JSON), Feldnamen ohne Rücksicht auf
    /// Groß-/Kleinschreibung wie die App. Kaputte Einträge zählen nicht.
    /// </summary>
    public static IReadOnlyList<LocalServer> Read(WhisperLoomSettings s)
    {
        if (s.Extra?.LastOrDefault(e => e.Key.Equals("local", StringComparison.OrdinalIgnoreCase)).Value is not { ValueKind: JsonValueKind.Object } local)
        {
            return [];
        }
        var servers = local.EnumerateObject().LastOrDefault(p => p.Name.Equals("servers", StringComparison.OrdinalIgnoreCase)).Value;
        if (servers.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var list = new List<LocalServer>();
        foreach (var item in servers.EnumerateArray())
        {
            try
            {
                if (item.Deserialize<LocalServer>(options) is { } server && !string.IsNullOrWhiteSpace(server.Name))
                {
                    list.Add(server);
                }
            }
            catch (JsonException)
            {
            }
        }
        return list;
    }
}
