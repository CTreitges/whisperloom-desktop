using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>Warum ein Zugang keinen Text verbessern kann (Port von <c>RefineBlock</c>).</summary>
public enum RefineBlock
{
    /// <summary>Der Anbieter hat keinen Chat-Endpunkt (ElevenLabs).</summary>
    NoChat,

    /// <summary>Reiner Erkennungs-Anbieter ohne eingetragenes Textmodell (Together, DeepInfra).</summary>
    NoModel,

    /// <summary>„Wie Erkennung“ bei lokaler Erkennung: es gibt keinen Online-Zugang zum Übernehmen.</summary>
    Offline,
}

/// <summary>
/// Aufgelöster Zugang zu einem Endpunkt — alles, was ein Aufruf braucht (Port von <c>ApiAccess</c>).
/// <see cref="ModelOption"/> kommt aus dem Katalog oder <see cref="ModelLists.OptionFor"/>; null = konservative Standards.
/// </summary>
public sealed record ApiAccess(
    string BaseUrl,
    string ApiKey,
    string Model,
    int ReadTimeoutMs,
    ProviderInfo Provider,
    ModelOption? ModelOption,
    bool SameAsOffline = false)
{
    /// <summary>Warum dieser Zugang keinen Text verbessern kann; null = er kann es (soweit bekannt).</summary>
    public RefineBlock? RefineBlock
    {
        get
        {
            if (SameAsOffline) return Api.RefineBlock.Offline;
            if (Provider.Api == ApiStyle.ElevenLabs) return Api.RefineBlock.NoChat;
            if (!Provider.HasLlm && string.IsNullOrWhiteSpace(Model)) return Api.RefineBlock.NoModel;
            return null;
        }
    }

    /// <summary>Ohne Key: ein Log darf ihn nie zeigen.</summary>
    public override string ToString() =>
        $"ApiAccess(provider={Provider.Id}, baseUrl={BaseUrl}, model={Model}, key={(ApiKey.Length > 0 ? "gesetzt" : "leer")}, " +
        $"readTimeoutMs={ReadTimeoutMs}, sameAsOffline={SameAsOffline})";
}

/// <summary>Vollständigkeit eines Zugangs (Port von <c>SetupState.kt</c>).</summary>
public static class SetupState
{
    /// <summary>Erkennungs-Zugang vollständig: Base-URL da und Key da (sofern der Anbieter einen will).</summary>
    public static bool SttComplete(string baseUrl, string apiKey, bool needsKey) =>
        !string.IsNullOrWhiteSpace(baseUrl) && (!needsKey || !string.IsNullOrWhiteSpace(apiKey));

    /// <summary>Wie <see cref="SttComplete"/> und mit Modell (Ollama lokal und eigener Server haben keinen Standard).</summary>
    public static bool LlmComplete(string baseUrl, string apiKey, bool needsKey, string model) =>
        SttComplete(baseUrl, apiKey, needsKey) && !string.IsNullOrWhiteSpace(model);

    /// <summary>Kann eine KI-Stufe mit diesem Zugang etwas ausrichten? Wie <see cref="LlmComplete"/>, dazu Chat-fähig.</summary>
    public static bool LlmReady(ApiAccess access) =>
        access.RefineBlock is null && LlmComplete(access.BaseUrl, access.ApiKey, access.Provider.NeedsKey, access.Model);

    /// <summary>Fallback der Erkennung brauchbar: vollständig und mit Modell.</summary>
    public static bool SttFallbackReady(ApiAccess access) =>
        SttComplete(access.BaseUrl, access.ApiKey, access.Provider.NeedsKey) && !string.IsNullOrWhiteSpace(access.Model);
}
