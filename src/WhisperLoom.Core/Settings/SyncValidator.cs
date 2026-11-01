using WhisperLoom.Core.Api;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;

namespace WhisperLoom.Core.Settings;

/// <summary>
/// Prüft Einstellungen vor dem Übernehmen eines Imports: Anbieter aus dem Katalog, Stufen- und Auswahlwerte,
/// URLs, Zahlenbereiche. Meldungen nennen den Pfad (<c>stt.providerId</c>) und den erlaubten Bereich.
/// Die Leseregeln der App (Unbekanntes gilt als Standard) bleiben davon unberührt — der Import ist nur strenger.
/// </summary>
public static class SyncValidator
{
    private static readonly string[] Cleanups = ["plain", "clean", "readable"];
    private static readonly string[] Forms = ["auto", "prose"];
    private static readonly string[] OfflineRefines = ["local", "online_local", "skip"];
    private static readonly string[] Engines = ["online", "offline"];

    public static IReadOnlyList<string> Validate(WhisperLoomSettings s)
    {
        var errors = new List<string>();
        if (s.Language != "auto" && !(s.Language.Length is 2 or 3 && s.Language.All(char.IsAsciiLetterLower)))
        {
            errors.Add("language: \"auto\" oder ISO-Code (z. B. \"de\")");
        }
        if (s.Engine is not null && !Engines.Contains(s.Engine))
        {
            errors.Add("engine: \"online\" oder \"offline\"");
        }

        Provider(errors, "stt.providerId", s.Stt.ProviderId, stt: true);
        Url(errors, "stt.baseUrl", s.Stt.BaseUrl, s.Stt.ProviderId);
        if (s.Stt.ReadTimeoutSec != 0 && s.Stt.ReadTimeoutSec is < 30 or > 1800)
        {
            errors.Add("stt.readTimeoutSec: 0 oder 30–1800");
        }
        Provider(errors, "sttFallback.providerId", s.SttFallback.ProviderId, stt: true);
        Url(errors, "sttFallback.baseUrl", s.SttFallback.BaseUrl, s.SttFallback.ProviderId);

        if (s.Llm.ProviderId != LlmSettings.Same)
        {
            Provider(errors, "llm.providerId", s.Llm.ProviderId, stt: false);
        }
        Url(errors, "llm.baseUrl", s.Llm.BaseUrl, s.Llm.ProviderId == LlmSettings.Same ? s.Stt.ProviderId : s.Llm.ProviderId);
        var modelStages = RefineModes.ModelStages.Select(m => m.Key()).ToList();
        foreach (var stage in s.Llm.StageModels.Keys.Where(k => !modelStages.Contains(k)))
        {
            errors.Add($"llm.stageModels.{stage}: Stufe unbekannt ({string.Join(", ", modelStages)})");
        }
        Provider(errors, "llmFallback.providerId", s.LlmFallback.ProviderId, stt: false);
        Url(errors, "llmFallback.baseUrl", s.LlmFallback.BaseUrl, s.LlmFallback.ProviderId);

        var stages = Enum.GetValues<RefineMode>().Select(m => m.Key()).Append("paragraphs").ToList();
        OneOf(errors, "refine.stage", s.Refine.Stage, stages);
        OneOf(errors, "refine.polishCleanup", s.Refine.PolishCleanup, Cleanups);
        OneOf(errors, "refine.summarizeForm", s.Refine.SummarizeForm, Forms);
        OneOf(errors, "share.stage", s.Share.Stage, RefineModes.Settings.Select(m => m.Key()));
        OneOf(errors, "share.polishCleanup", s.Share.PolishCleanup, Cleanups);
        OneOf(errors, "share.summarizeForm", s.Share.SummarizeForm, Forms);
        OneOf(errors, "offline.refine", s.Offline.Refine, OfflineRefines);
        if (!HistorySettings.Sizes.Contains(s.History.Size))
        {
            errors.Add($"history.size: eine von {string.Join(", ", HistorySettings.Sizes)}");
        }
        return errors;
    }

    private static void Provider(List<string> errors, string path, string id, bool stt)
    {
        var p = ProviderCatalog.Find(id);
        if (p is null || (stt ? !p.HasStt : !p.HasLlm))
        {
            var allowed = (stt ? ProviderCatalog.SttProviders : ProviderCatalog.LlmProviders).Select(x => x.Id);
            errors.Add($"{path}: Anbieter unbekannt ({string.Join(", ", allowed)})");
        }
    }

    /// <summary>
    /// Leer oder eine Adresse, die <see cref="ServerUrlCheck.Check"/> durchlässt; unverschlüsselt über das Internet
    /// (dort nur ein Hinweis) ist hier ein Fehler — eine fremde Datei soll keinen Key im Klartext verschicken.
    /// </summary>
    private static void Url(List<string> errors, string path, string url, string providerId)
    {
        if (url == "")
        {
            return;
        }
        if (!(Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme is "http" or "https"))
        {
            errors.Add($"{path}: leer oder http(s)-URL");
        }
        else if (ProviderCatalog.Find(providerId) is { } provider &&
                 ServerUrlCheck.Check(url, provider) is { } problem &&
                 (problem.Severity == ServerUrlCheck.Severity.Error || problem.Message == ServerUrlCheck.MsgPublicHttp))
        {
            errors.Add($"{path}: {problem.Message}");
        }
    }

    private static void OneOf(List<string> errors, string path, string value, IEnumerable<string> allowed)
    {
        var list = allowed.ToList();
        if (!list.Contains(value))
        {
            errors.Add($"{path}: eines von {string.Join(", ", list)}");
        }
    }
}
