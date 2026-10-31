using WhisperLoom.Core.Api;
using WhisperLoom.Core.Audio;
using WhisperLoom.Core.Contracts;
using WhisperLoom.Core.Contracts.Settings;
using WhisperLoom.Core.Prompts;
using WhisperLoom.Core.Text;

namespace WhisperLoom.Core.Pipeline;

/// <summary>
/// Die echten Text-Bausteine für die <see cref="DictationPipeline"/> (WLD-P1a): Prompt-Builder, Regeln, Stille-Schnitt und
/// Kontext-Wörter. App und Tests verdrahten die Pipeline über dieselben Teile.
/// </summary>
public static class CoreText
{
    public static IRefinePromptBuilder Prompts { get; } = new RefinePrompt();

    public static ITextPolisher Polisher { get; } = new TextPolisher();

    public static AudioBuffer TrimSilence(AudioBuffer audio) => AudioUtils.TrimSilence(audio);

    /// <summary>Liste und Datei, bei jedem Diktat neu gelesen (Kotlin: <c>VocabularySource.prompt</c>).</summary>
    public static string VocabularyPrompt(WhisperLoomSettings settings) => VocabularySource.Prompt(settings.Stt).Text;

    /// <summary>Optionen mit allen echten Bausteinen; <paramref name="cache"/> = Merkliste im Datenordner.</summary>
    public static DictationPipelineOptions Options(ModelCache? cache = null) => new()
    {
        ModelCache = cache,
        TrimSilence = TrimSilence,
        VocabularyPrompt = VocabularyPrompt,
    };
}
