# Test-Mapping: Core-API und Pipeline (WLD-P1b)

Zuordnung Kotlin-Testklasse (Android `main` 8e1caeb, `app/src/test*`) → C#-Testklasse, geprüft mit `tools/test_parity.py`.
`portiert` verlangt mindestens so viele aktive C#-Fälle wie Kotlin-`@Test` (Fälle mit `Skip` zählen nicht).
Die Spalte „Begründung“ nennt bei portierten Klassen die Abbildung auf den Desktop.

| Kotlin-Klasse | C#-Klasse | Status | Begründung |
|---|---|---|---|
| RefineDecisionTest | WhisperLoom.Core.Tests.Pipeline.RefineDecisionTest | portiert | `TextModelCatalog.anyFits` (RAM des Android-Geräts) entfällt; „Textmodell passt“ = lokaler Textverbesserer eingerichtet. |
| api/TextCleanupTest | WhisperLoom.Core.Tests.Api.TextCleanupTest | portiert | Füllsilben als feste Liste; der Fall mit der echten Liste (`TextPolisher.builtinFillers`) ist Skip „Needs P1a“. |
| api/PromptCleanupTest | WhisperLoom.Core.Tests.Api.PromptCleanupTest | portiert | |
| ChatPayloadTest | WhisperLoom.Core.Tests.Api.ChatPayloadTest | portiert | Keys als `test-key-not-real-<n>`. |
| TranscriptionRequestTest | WhisperLoom.Core.Tests.Api.TranscriptionRequestTest | portiert | |
| ElevenLabsSttTest | WhisperLoom.Core.Tests.Api.ElevenLabsSttTest | portiert | Kappung über `Vocabulary.prompt` (WLD-P1a) als Skip; der ElevenLabs-Teil (angeschnittenes Stück fällt weg) aktiv. |
| ProviderCatalogTest | WhisperLoom.Core.Tests.Api.ProviderCatalogTest | portiert | Katalog aus `providers.json`; `Prefs.DEFAULT_*` → Standard-Anbieter der Einstellungen. |
| AccessResolverTest | WhisperLoom.Core.Tests.Api.AccessResolverTest | portiert | |
| ApiErrorsTest | WhisperLoom.Core.Tests.Api.ApiErrorsTest | portiert | Java-Netzfehler → .NET (`HttpRequestError`, `TimeoutException`, `UriFormatException`); Android-Cleartext-Meldung entfällt. `detail`/`body` stehen nicht im Vertragstyp — `detail` steckt in der Meldung. |
| RefinePipelineTest | WhisperLoom.Core.Tests.Pipeline.RefinePipelineTest | portiert | Prefs → `WhisperLoomSettings`, Robolectric-Netz → `NetworkCheck`, LiteRT-Fake → `ILocalRefiner`-Fake, Vorwärmen → `ShouldWarmUpLocalAsync`. „Also hallo welt“ (TextPolisher) als Skip; aktiv geprüft: Rohtext geht mit vollen Regeln an den Polisher. |
| FallbackPipelineTest | WhisperLoom.Core.Tests.Pipeline.FallbackPipelineTest | portiert | Wie RefinePipelineTest; Kotlin-`suppressed` → `FallbackBackend.Suppressed`. |
| FallbackBackendTest | WhisperLoom.Core.Tests.Pipeline.FallbackBackendTest | portiert | Thread-Interrupt → abgebrochener `CancellationToken`. |
| TranscriptionEngineTest | WhisperLoom.Core.Tests.Pipeline.TranscriptionEngineTest | portiert | Offline-Modelldatei → `ILocalStt.IsReadyAsync`; Prompt-Texte über einen Fake-Builder (welche Stufe/Absätze/Füllwörter verlangt werden), die Kotlin-Texte als Skip „Needs P1a“; Vokabular-Datei (VocabularySource, P1a) → `VocabularyPrompt`-Delegate. |
| StageModelTest | WhisperLoom.Core.Tests.Pipeline.StageModelTest | portiert | |
| TextRefinerRetryTest | WhisperLoom.Core.Tests.Api.TextRefinerRetryTest | portiert | „Zugang prüfen“ (AccessTest) → `TextRefiner` mit `ModelCache.RememberNoTemperature`. |
| TemperatureMemoTest | WhisperLoom.Core.Tests.Pipeline.TemperatureMemoTest | portiert | Zusätzlich: Merkliste übersteht einen Neustart (Datei, ohne Key). |
| HttpTest | WhisperLoom.Core.Tests.Api.HttpTest | portiert | JDK-HttpServer → `FakeServer` (TcpListener); Assert auf `e.body` entfällt (Vertrag). |
| ModelListsTest | WhisperLoom.Core.Tests.Api.ModelListsTest | portiert | |
| OllamaApiTest | WhisperLoom.Core.Tests.Api.OllamaApiTest | portiert | |
| llm/LocalRefinerTest | WhisperLoom.Core.Tests.Api.LocalRefinerTest | portiert | Lokal = Ollama (Goal): `TextRefiner` mit Ollama-Zugang gegen Fake-Ollama. LiteRT-KV-Grenze (`MSG_TOO_LONG`) → Ollama schneidet ab (`done_reason: length`) → `MsgTruncated`. Prompt-Texte als Skip „Needs P1a“. |

## Tests mit `[Trait("Needs", "P1a")]`

Sie stehen mit der Kotlin-Erwartung im Code und sind per `Skip` aus; WLD-INT verdrahtet `P1a.Prompts`/`P1a.Polisher`
(`tests/WhisperLoom.Core.Tests/Support/TextFakes.cs`) mit den echten Klassen aus WLD-P1a und entfernt das `Skip`.
