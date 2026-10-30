# Test-Mapping: Core-API und Pipeline (WLD-P1b)

Zuordnung Kotlin-Testklasse (Android `main` 8e1caeb, `app/src/test*`) → C#-Testklasse, geprüft mit `tools/test_parity.py`.
`portiert` verlangt mindestens so viele aktive C#-Fälle wie Kotlin-`@Test` (Fälle mit `Skip` zählen nicht).
Die Spalte „Begründung“ nennt bei portierten Klassen die Abbildung auf den Desktop.

| Kotlin-Klasse | C#-Klasse | Status | Begründung |
|---|---|---|---|
| RefineDecisionTest | WhisperLoom.Core.Tests.Pipeline.RefineDecisionTest | portiert | `TextModelCatalog.anyFits` (RAM des Android-Geräts) entfällt; „Textmodell passt“ = lokaler Textverbesserer eingerichtet. |
| api/TextCleanupTest | WhisperLoom.Core.Tests.Api.TextCleanupTest | portiert | Füllsilben als feste Liste; ein Fall mit der echten Liste (`TextPolisher.builtinFillers`). |
| api/PromptCleanupTest | WhisperLoom.Core.Tests.Api.PromptCleanupTest | portiert | |
| ChatPayloadTest | WhisperLoom.Core.Tests.Api.ChatPayloadTest | portiert | Keys als `test-key-not-real-<n>`. |
| TranscriptionRequestTest | WhisperLoom.Core.Tests.Api.TranscriptionRequestTest | portiert | |
| ElevenLabsSttTest | WhisperLoom.Core.Tests.Api.ElevenLabsSttTest | portiert | Kappung über `Vocabulary.prompt` und der ElevenLabs-Teil (angeschnittenes Stück fällt weg) je als eigener Fall. |
| ProviderCatalogTest | WhisperLoom.Core.Tests.Api.ProviderCatalogTest | portiert | Katalog aus `providers.json`; `Prefs.DEFAULT_*` → Standard-Anbieter der Einstellungen. |
| AccessResolverTest | WhisperLoom.Core.Tests.Api.AccessResolverTest | portiert | |
| ApiErrorsTest | WhisperLoom.Core.Tests.Api.ApiErrorsTest | portiert | Java-Netzfehler → .NET (`HttpRequestError`, `TimeoutException`, `UriFormatException`); Android-Cleartext-Meldung entfällt. `detail`/`body` stehen nicht im Vertragstyp — `detail` steckt in der Meldung. |
| RefinePipelineTest | WhisperLoom.Core.Tests.Pipeline.RefinePipelineTest | portiert | Prefs → `WhisperLoomSettings`, Robolectric-Netz → `NetworkCheck`, LiteRT-Fake → `ILocalRefiner`-Fake, Vorwärmen → `ShouldWarmUpLocalAsync`. Meist geprüft: Rohtext geht mit vollen Regeln an den Polisher; „Also hallo welt“ mit echten Regeln (`RealText`). |
| FallbackPipelineTest | WhisperLoom.Core.Tests.Pipeline.FallbackPipelineTest | portiert | Wie RefinePipelineTest; Kotlin-`suppressed` → `FallbackBackend.Suppressed`. |
| FallbackBackendTest | WhisperLoom.Core.Tests.Pipeline.FallbackBackendTest | portiert | Thread-Interrupt → abgebrochener `CancellationToken`. |
| TranscriptionEngineTest | WhisperLoom.Core.Tests.Pipeline.TranscriptionEngineTest | portiert | Offline-Modelldatei → `ILocalStt.IsReadyAsync`; Prompt-Texte über einen Fake-Builder (welche Stufe/Absätze/Füllwörter verlangt werden), die Kotlin-Texte mit echtem Builder und Regeln (`RealText`, `CoreText`); Vokabular-Datei → `CoreText.VocabularyPrompt`. |
| StageModelTest | WhisperLoom.Core.Tests.Pipeline.StageModelTest | portiert | |
| TextRefinerRetryTest | WhisperLoom.Core.Tests.Api.TextRefinerRetryTest | portiert | „Zugang prüfen“ (AccessTest) → `TextRefiner` mit `ModelCache.RememberNoTemperature`. |
| TemperatureMemoTest | WhisperLoom.Core.Tests.Pipeline.TemperatureMemoTest | portiert | Zusätzlich: Merkliste übersteht einen Neustart (Datei, ohne Key). |
| HttpTest | WhisperLoom.Core.Tests.Api.HttpTest | portiert | JDK-HttpServer → `FakeServer` (TcpListener); Assert auf `e.body` entfällt (Vertrag). |
| ModelListsTest | WhisperLoom.Core.Tests.Api.ModelListsTest | portiert | |
| OllamaApiTest | WhisperLoom.Core.Tests.Api.OllamaApiTest | portiert | |
| llm/LocalRefinerTest | WhisperLoom.Core.Tests.Api.LocalRefinerTest | portiert | Lokal = Ollama (Goal): `TextRefiner` mit Ollama-Zugang gegen Fake-Ollama. LiteRT-KV-Grenze (`MSG_TOO_LONG`) → Ollama schneidet ab (`done_reason: length`) → `MsgTruncated`. Prompt-Texte mit `CoreText.Prompts`. |

## Echte Text-Bausteine (WLD-INT)

Die zwölf Fälle, die bis WLD-INT per `Skip` auf WLD-P1a warteten, laufen jetzt mit der Kotlin-Erwartung über die Pipeline:
`PipelineHarness.RealText` bzw. `CoreText` (`src/WhisperLoom.Core/Pipeline/CoreText.cs`) verdrahten `RefinePrompt`,
`TextPolisher`, `AudioUtils.TrimSilence` und `VocabularySource.Prompt` — dieselbe Verdrahtung wie in der App.
