using System.Text.Json;
using System.Text.Json.Nodes;
using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Richtlinie der IT aus dem Provisioning (<c>policy</c>), gespeichert getrennt von settings.json in policy.json.
/// Gesperrte Pfade (<c>stt.providerId</c>, ganze Abschnitte wie <c>llm</c>, Keys als <c>secrets.stt</c>) sind in der
/// Oberfläche ausgegraut und werden von einem Import des Nutzers nicht geändert.
/// </summary>
public sealed record PolicySettings
{
    /// <summary>Keine Richtlinie: nichts gesperrt, alle Anbieter erlaubt.</summary>
    public static PolicySettings None { get; } = new();

    /// <summary>Gesperrte Pfade; ein Pfad sperrt auch alles darunter.</summary>
    public IReadOnlyList<string> Locked { get; init; } = [];

    /// <summary>Verlauf fest an bzw. aus; null = Nutzer entscheidet.</summary>
    public bool? HistoryEnabled { get; init; }

    /// <summary>Automatische Updates; null = Standard (an). Wertet die Update-Funktion aus (Phase 6).</summary>
    public bool? UpdatesEnabled { get; init; }

    /// <summary>Erlaubte Anbieter-IDs; null = alle.</summary>
    public IReadOnlyList<string>? AllowedProviders { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive => Locked.Count > 0 || HistoryEnabled is not null || UpdatesEnabled is not null || AllowedProviders is not null;

    public bool IsLocked(string path) =>
        Locked.Any(l => path == l || path.StartsWith(l + ".", StringComparison.Ordinal))
        || (HistoryEnabled is not null && path == "history.enabled");

    /// <summary>„wie Erkennung“ (<see cref="LlmSettings.Same"/>) ist immer erlaubt, es nutzt den erlaubten Erkennungs-Anbieter.</summary>
    public bool IsProviderAllowed(string providerId) =>
        AllowedProviders is null || providerId == LlmSettings.Same || AllowedProviders.Contains(providerId);

    /// <summary>Setzt die festen Werte (Verlauf) in <paramref name="settings"/>.</summary>
    public void Enforce(WhisperLoomSettings settings)
    {
        if (HistoryEnabled is { } history)
        {
            settings.History.Enabled = history;
        }
    }

    /// <summary>
    /// Genutzte Anbieter, die die Richtlinie nicht erlaubt (abgeschaltete Fallbacks zählen nicht). Mit Freigabeliste
    /// zählt bei Anbietern mit fester Adresse auch eine eigene Base-URL zu einem anderen Host als Verstoß — sonst gingen
    /// Diktate und der Key eines erlaubten Anbieters an einen beliebigen Server. Eigener Server und Ollama haben keine
    /// feste Adresse; wer sie freigibt, gibt jeden Server frei.
    /// </summary>
    public IReadOnlyList<string> Violations(WhisperLoomSettings s)
    {
        var same = s.Llm.ProviderId == LlmSettings.Same;
        var used = new List<(string Path, string Id, string? Url)>
        {
            ("stt", s.Stt.ProviderId, s.Stt.BaseUrl),
            ("llm", s.Llm.ProviderId, same ? null : s.Llm.BaseUrl), // „wie Erkennung“ nutzt die Adresse der Erkennung
        };
        if (s.SttFallback.Enabled)
        {
            used.Add(("sttFallback", s.SttFallback.ProviderId, s.SttFallback.BaseUrl));
        }
        if (s.LlmFallback.Enabled)
        {
            used.Add(("llmFallback", s.LlmFallback.ProviderId, s.LlmFallback.BaseUrl));
        }
        var violations = used.Where(u => !IsProviderAllowed(u.Id))
            .Select(u => $"{u.Path}.providerId: Anbieter \"{u.Id}\" ist von IT nicht freigegeben").ToList();
        if (AllowedProviders is not null)
        {
            violations.AddRange(used
                .Where(u => IsProviderAllowed(u.Id) && !IsAddressAllowed(u.Id, u.Url))
                .Select(u => $"{u.Path}.baseUrl: von IT ist nur der Server von \"{u.Id}\" freigegeben ({KeyRoutes.Origin(u.Url!)})"));
        }
        return violations;
    }

    /// <summary>
    /// Darf ein Key an diesen Zugang gehen (auch „Zugang prüfen“)? Anbieter freigegeben und, bei Freigabeliste und fester
    /// Adresse des Anbieters, leere bzw. gleiche Adresse wie im Katalog.
    /// </summary>
    public bool AllowsTarget(string providerId, string? baseUrl) => IsProviderAllowed(providerId) && IsAddressAllowed(providerId, baseUrl);

    private bool IsAddressAllowed(string providerId, string? baseUrl) =>
        AllowedProviders is null || string.IsNullOrWhiteSpace(baseUrl) || ProviderCatalog.Find(providerId) is not { } p || p.NeedsUrl ||
        KeyRoutes.Origin(baseUrl) == KeyRoutes.Origin(p.BaseUrl);

    /// <summary>
    /// Liest den Abschnitt <c>policy</c> einer Provisioning-Datei. <c>lockSettings</c>: <c>true</c> sperrt alle Pfade,
    /// die die Datei setzt (<paramref name="provisionedPaths"/>), eine Liste sperrt genau diese Pfade, <c>false</c> nichts.
    /// </summary>
    /// <exception cref="SyncFormatException">Falscher Typ eines Feldes.</exception>
    public static PolicySettings FromProvisioning(JsonObject policy, IEnumerable<string> provisionedPaths)
    {
        IReadOnlyList<string> locked = [];
        switch (policy["lockSettings"])
        {
            case null:
                break;
            case JsonValue v when v.GetValueKind() is JsonValueKind.True:
                locked = [.. provisionedPaths];
                break;
            case JsonValue v when v.GetValueKind() is JsonValueKind.False:
                break;
            case JsonArray a:
                locked = Strings(a, "policy.lockSettings");
                break;
            default:
                throw new SyncFormatException("policy.lockSettings: true, false oder eine Liste von Pfaden");
        }
        return new PolicySettings
        {
            Locked = locked,
            HistoryEnabled = Bool(policy, "historyEnabled"),
            UpdatesEnabled = Bool(policy, "updatesEnabled"),
            AllowedProviders = policy["allowedProviders"] switch
            {
                null => null,
                JsonArray a => Strings(a, "policy.allowedProviders"),
                _ => throw new SyncFormatException("policy.allowedProviders: Liste von Anbieter-IDs"),
            },
        };
    }

    private static bool? Bool(JsonObject o, string name) => o[name] switch
    {
        null => null,
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
        _ => throw new SyncFormatException($"policy.{name}: true oder false"),
    };

    private static List<string> Strings(JsonArray a, string path)
    {
        var list = new List<string>();
        foreach (var n in a)
        {
            if (n is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))
            {
                throw new SyncFormatException($"{path}: nur nicht leere Texte");
            }
            list.Add(s.Trim());
        }
        return list;
    }
}

/// <summary>policy.json im Datenordner: dieselben Felder, <c>locked</c> als aufgelöste Pfadliste.</summary>
public static class PolicyJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(PolicySettings policy) => JsonSerializer.Serialize(policy, Options);

    /// <exception cref="JsonException">Kein gültiges JSON oder <c>null</c> (zählt wie unlesbar: key und update brechen ab).</exception>
    public static PolicySettings Deserialize(string json) =>
        JsonSerializer.Deserialize<PolicySettings>(json, Options) ?? throw new JsonException("policy.json enthält null");
}
