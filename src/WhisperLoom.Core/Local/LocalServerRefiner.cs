using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Local;

/// <summary>
/// Lokale Textverbesserung über einen OpenAI-kompatiblen Server auf diesem Gerät bzw. im LAN (<c>local.textEngine =
/// "server"</c>: LM Studio, llama.cpp, Ollama über <c>/v1</c>). Derselbe Auftrag wie online (<see cref="TextRefiner"/>,
/// Prompts unverändert), nur der Transport ist lokal; nie mit Key. Modell "" = erstes Textmodell der Liste des Servers.
/// </summary>
public sealed class LocalServerRefiner(
    HttpClient http,
    Func<LocalServer?> server,
    IRefinePromptBuilder prompts,
    TextCleanup cleanup) : ILocalRefiner
{
    public const string MsgNoServer = "Kein lokaler Text-Server eingetragen — unter „Lokale Modelle“ hinzufügen";

    private const int ListTimeoutMs = 3_000;

    public static string MsgUnreachable(string name, string url) => $"Lokaler Text-Server „{name}“ nicht erreichbar ({url})";

    public static string MsgNoModel(string name) => $"Lokaler Text-Server „{name}“ meldet kein Textmodell";

    private string? _lastModel;

    /// <summary>Zuletzt genutztes Modell (bei "" das erste der Liste), sonst das eingetragene.</summary>
    public string ModelLabel => _lastModel ?? server()?.Model ?? "";

    public async Task<bool> IsReadyAsync(CancellationToken ct)
    {
        if (server() is not { } s) return false;
        return await ModelAsync(s, ct).ConfigureAwait(false) is { Length: > 0 };
    }

    public async Task<string> RefineAsync(string raw, string language, Refinement refinement, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(raw) || refinement.Mode == RefineMode.Off) return raw;
        var s = server() ?? throw new LocalNotAvailableException(MsgNoServer);
        var model = await ModelAsync(s, ct).ConfigureAwait(false) ?? throw new LocalNotAvailableException(MsgUnreachable(s.Name, s.BaseUrl));
        if (model.Length == 0) throw new LocalNotAvailableException(MsgNoModel(s.Name));
        _lastModel = model;
        return await RefineAtAsync(http, Access(s, model), raw, language, refinement, prompts, cleanup, ct).ConfigureAwait(false);
    }

    /// <summary>Zugang zum Server als Eintrag <c>local:&lt;name&gt;</c> (feste Adresse, ohne Key).</summary>
    public static ApiAccess Access(LocalServer s, string model)
    {
        var directory = new ProviderDirectory([new LocalServerSource([s])]);
        var none = AccessResolver.ResolveStt("", "", "", "");
        return AccessResolver.ResolveLlm(none, LocalServerSource.LocalPrefix + s.Name, "", "", model, providers: directory);
    }

    /// <summary>Gemeinsamer Weg mit dem mitgelieferten Modell: <see cref="TextRefiner"/>, Netzfehler = nicht verfügbar.</summary>
    public static async Task<string> RefineAtAsync(
        HttpClient http, ApiAccess access, string raw, string language, Refinement refinement, IRefinePromptBuilder prompts, TextCleanup cleanup, CancellationToken ct)
    {
        try
        {
            return await new TextRefiner(access, http, prompts, cleanup).RefineAsync(raw, language, refinement, ct).ConfigureAwait(false);
        }
        catch (ApiNetworkException) when (!ct.IsCancellationRequested)
        {
            throw new LocalNotAvailableException(MsgUnreachable(access.Provider.Name, access.BaseUrl));
        }
    }

    /// <summary>Eingetragenes Modell, sonst das erste der Liste; null = Server nicht erreichbar. Wirft nur bei Abbruch.</summary>
    private async Task<string?> ModelAsync(LocalServer s, CancellationToken ct)
    {
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(ListTimeoutMs);
            var models = await ModelLists.LoadAsync(http, Access(s, s.Model), ModelKind.Llm, limit.Token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(s.Model) ? models.FirstOrDefault()?.Id ?? "" : s.Model.Trim();
        }
        catch (Exception e) when (e is ApiNetworkException or ApiHttpException or ModelListException or ApiNotConfiguredException or OperationCanceledException
                                  && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
