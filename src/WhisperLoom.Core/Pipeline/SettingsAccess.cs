using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Pipeline;

/// <summary>Die vier Keys eines Auftrags aus dem <see cref="ISecretStore"/>; "" = keiner. <see cref="ToString"/> ohne Werte.</summary>
public sealed record AccessKeys(string Stt = "", string SttFallback = "", string Llm = "", string LlmFallback = "")
{
    public override string ToString() => "AccessKeys(…)";
}

/// <summary>
/// Liest Stufe und Zugänge aus den Einstellungen (Port der Zugangs- und Stufen-Getter von <c>Prefs.kt</c>:
/// <c>sttAccess</c>, <c>llmAccess</c>, <c>sttFallbackAccess</c>, <c>llmFallbackAccess</c>, <c>refinementFor</c>).
/// </summary>
public static class SettingsAccess
{
    /// <summary>Altwert der früheren Stufe „Absätze“ — gilt als Glätten.</summary>
    private const string LegacyParagraphs = "paragraphs";

    public static Engine? EngineOf(WhisperLoomSettings s) => Engines.FromKey(s.Engine);

    /// <summary>Stufe des Diktats: "paragraphs" → Glätten, „Prompt“ nur eingeschaltet (sonst Glätten), Unbekanntes → aus.</summary>
    public static RefineMode DictationStage(WhisperLoomSettings s)
    {
        var mode = s.Refine.Stage == LegacyParagraphs ? RefineMode.Polish : RefineModes.FromKey(s.Refine.Stage);
        return mode == RefineMode.Prompt && !s.Refine.PromptEnabled ? RefineMode.Polish : mode;
    }

    /// <summary>Die wirksame Verarbeitung des Wegs (<see cref="Refinement.Of"/>).</summary>
    public static Refinement RefinementFor(WhisperLoomSettings s, RefineWay way)
    {
        if (way == RefineWay.Dictation)
        {
            var stage = DictationStage(s);
            var paragraphs = stage switch
            {
                RefineMode.Polish or RefineMode.Readable => s.Refine.ParagraphsPolish,
                RefineMode.Beautify => s.Refine.ParagraphsBeautify,
                _ => true,
            };
            return Refinement.Of(way, stage, RefineKeys.CleanupFromKey(s.Refine.PolishCleanup), paragraphs, RefineKeys.FormFromKey(s.Refine.SummarizeForm));
        }
        var shareStage = RefineModes.FromKey(s.Share.Stage);
        if (!RefineModes.Settings.Contains(shareStage)) shareStage = RefineMode.Off;
        return Refinement.Of(way, shareStage, RefineKeys.CleanupFromKey(s.Share.PolishCleanup), true, RefineKeys.FormFromKey(s.Share.SummarizeForm));
    }

    /// <summary>Eigenes Modell der Stufe von <paramref name="mode"/> („Lesbar“ = Glätten); "" = Standard.</summary>
    public static string LlmModelFor(WhisperLoomSettings s, RefineMode? mode) =>
        mode?.ModelStage() is { } stage ? s.Llm.StageModels.GetValueOrDefault(stage.Key()) ?? "" : "";

    public static ApiAccess Stt(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null) =>
        AccessResolver.ResolveStt(s.Stt.ProviderId, s.Stt.BaseUrl, keys.Stt, s.Stt.Model, s.Stt.ReadTimeoutSec, lookup);

    /// <summary>Zugang der Textverbesserung; mit <paramref name="mode"/> gilt das Modell dieser Stufe.</summary>
    public static ApiAccess Llm(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null, RefineMode? mode = null) =>
        AccessResolver.ResolveLlm(Stt(s, keys, lookup), s.Llm.ProviderId, s.Llm.BaseUrl, keys.Llm, s.Llm.Model, lookup,
            sttOffline: EngineOf(s) == Engine.Offline, stageModel: LlmModelFor(s, mode), stage: mode);

    /// <summary>Fallback der Erkennung; null = Schalter aus. Bereit: <see cref="SetupState.SttFallbackReady"/>.</summary>
    public static ApiAccess? SttFallback(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null) =>
        !s.SttFallback.Enabled
            ? null
            : AccessResolver.ResolveSttFallback(Stt(s, keys, lookup), s.SttFallback.ProviderId, s.SttFallback.BaseUrl, keys.SttFallback,
                s.SttFallback.Model, lookup);

    /// <summary>Fallback der Textverbesserung für die Stufe von <paramref name="mode"/>; null = Schalter aus.</summary>
    public static ApiAccess? LlmFallback(WhisperLoomSettings s, AccessKeys keys, IServerModelLookup? lookup = null, RefineMode? mode = null) =>
        !s.LlmFallback.Enabled
            ? null
            : AccessResolver.ResolveLlmFallback(Stt(s, keys, lookup), Llm(s, keys, lookup, mode), s.LlmFallback.ProviderId,
                s.LlmFallback.BaseUrl, keys.LlmFallback, s.LlmFallback.Model, lookup, EngineOf(s) == Engine.Offline, mode);

    public static async Task<AccessKeys> KeysAsync(ISecretStore secrets, CancellationToken ct) => new(
        await secrets.GetAsync(SecretNames.Stt, ct).ConfigureAwait(false) ?? "",
        await secrets.GetAsync(SecretNames.SttFallback, ct).ConfigureAwait(false) ?? "",
        await secrets.GetAsync(SecretNames.Llm, ct).ConfigureAwait(false) ?? "",
        await secrets.GetAsync(SecretNames.LlmFallback, ct).ConfigureAwait(false) ?? "");
}
