using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>Genutzte Anbieter, die die Richtlinie nicht erlaubt (abgeschaltete Fallbacks zählen nicht).</summary>
    public IReadOnlyList<string> Violations(WhisperLoomSettings s)
    {
        var used = new List<(string Path, string Id)> { ("stt.providerId", s.Stt.ProviderId), ("llm.providerId", s.Llm.ProviderId) };
        if (s.SttFallback.Enabled)
        {
            used.Add(("sttFallback.providerId", s.SttFallback.ProviderId));
        }
        if (s.LlmFallback.Enabled)
        {
            used.Add(("llmFallback.providerId", s.LlmFallback.ProviderId));
        }
        return [.. used.Where(u => !IsProviderAllowed(u.Id)).Select(u => $"{u.Path}: Anbieter \"{u.Id}\" ist von IT nicht freigegeben")];
    }

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

    /// <exception cref="JsonException">Kein gültiges JSON.</exception>
    public static PolicySettings Deserialize(string json) =>
        JsonSerializer.Deserialize<PolicySettings>(json, Options) ?? PolicySettings.None;
}
