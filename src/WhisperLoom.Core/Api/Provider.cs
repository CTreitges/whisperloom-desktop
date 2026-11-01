using WhisperLoom.Core.Contracts;

namespace WhisperLoom.Core.Api;

/// <summary>Abgeleitete Eigenschaften eines Anbieters (Port der Getter von <c>api/Provider.kt</c>).</summary>
public static class ProviderExtensions
{
    extension(ProviderInfo p)
    {
        /// <summary>Katalog-ID, deren Regeln gelten (Modell-Listen, Heuristiken): <see cref="ProviderInfo.Family"/> bzw. die ID.</summary>
        public string Rules => string.IsNullOrEmpty(p.Family) ? p.Id : p.Family;

        /// <summary>Eintrag außerhalb des Katalogs (eigener Anbieter, lokaler Server): Adresse fest, Fähigkeit über die Listen.</summary>
        public bool IsEntry => !string.IsNullOrEmpty(p.Family);

        /// <summary>OpenAI-kompatibler eigener Server (Katalog „custom“ oder ein Eintrag mit diesen Regeln).</summary>
        public bool IsCustom => p.Rules == ProviderCatalog.CustomId;

        public bool IsOllama => p.Api == ApiStyle.Ollama;

        /// <summary>Der Nutzer trägt die Server-Adresse selbst ein (eigener Server, Ollama im Heimnetz).</summary>
        public bool NeedsUrl => string.IsNullOrWhiteSpace(p.BaseUrl) && !p.IsEntry;

        /// <summary>
        /// Taugt für die Erkennung (eigener Server: Modell-ID frei). Ollama kann kein Audio. Einträge: Liste vorhanden
        /// (auch leer = Modelle vom Server).
        /// </summary>
        public bool HasStt => p.IsEntry ? p.SttModels is not null : p.IsCustom || p.SttModels is { Count: > 0 };

        /// <summary>Taugt für die Textverbesserung. Ollama-Modelle kommen vom Server selbst (/api/tags).</summary>
        public bool HasLlm => p.IsEntry ? p.LlmModels is not null : p.IsCustom || p.IsOllama || p.LlmModels is { Count: > 0 };

        /// <summary>Erstes Modell der Liste = Empfehlung; "" beim eigenen Server.</summary>
        public string DefaultSttModel => p.SttModels is { Count: > 0 } m ? m[0].Id : "";

        public string DefaultLlmModel => p.LlmModels is { Count: > 0 } m ? m[0].Id : "";

        /// <summary>
        /// Empfehlung für eine Stufe: zum Umformulieren (Verschönern, Zusammenfassen, Prompt)
        /// <see cref="ProviderInfo.RewriteLlmModel"/>, sonst — Glätten, Lesbar, ohne Stufe — <see cref="DefaultLlmModel"/>.
        /// </summary>
        public string RecommendedLlmModel(RefineMode? mode) => mode?.ModelStage() switch
        {
            RefineMode.Beautify or RefineMode.Summarize or RefineMode.Prompt =>
                string.IsNullOrWhiteSpace(p.RewriteLlmModel) ? p.DefaultLlmModel : p.RewriteLlmModel,
            _ => p.DefaultLlmModel,
        };

        public ModelOption? SttModel(string id) => p.SttModels?.FirstOrDefault(m => m.Id == id);

        public ModelOption? LlmModel(string id) => p.LlmModels?.FirstOrDefault(m => m.Id == id);
    }
}
