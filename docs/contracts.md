# Verträge (Phase 0)

Stand 2026-10-30. Quelle: `src/WhisperLoom.Core/Contracts/` (Namespace `WhisperLoom.Core.Contracts`).
Die Verträge trennen Core-Text, Core-API und App-Shell, damit diese Teile parallel entstehen können.
**Änderungen nur additiv** (neue Member mit Standardwert, neue Typen). Wer mehr braucht, fragt an
(`VERTRAG? <Änderung + Grund>`), statt still zu ändern.

## Gemeinsame Regeln

- **Threading:** Alles mit I/O ist `async` und nimmt ein `CancellationToken`. `OperationCanceledException`
  (bzw. `TaskCanceledException`) heißt **nur** „Token abgebrochen“ (`ct.IsCancellationRequested`) und wird nie in einen
  anderen Fehler umgedeutet. Ein HttpClient-Zeitlimit wirft ebenfalls `TaskCanceledException`, ohne dass der Token
  abgebrochen ist — Implementierungen wandeln das in `ApiNetworkException` um, damit der Fallback greift.
  Implementierungen blockieren den UI-Thread nicht und sind ohne Synchronisationskontext aufrufbar
  (`ConfigureAwait(false)`). Reine Funktionen (`IRefinePromptBuilder`, `ITextPolisher`) sind synchron und
  threadsicher.
- **Fehler:** Ausnahmetypen aus `Errors.cs` (Namen wie `api/ApiErrors.kt`):

  | Typ | Bedeutung |
  |---|---|
  | `ApiNotConfiguredException` | Zugang unvollständig oder Anbieter kann die Aufgabe nicht — ohne Anfrage |
  | `ApiHttpException` (`StatusCode`) | Server antwortete mit Fehlerstatus |
  | `ApiNetworkException` | DNS, Verbindung, TLS, Zeitlimit |
  | `RefineRejectedException` | Modell hat geantwortet statt bearbeitet, abgeschnitten oder leer |
  | `LocalNotAvailableException` | Begleitprozess, lokales Modell oder Ollama fehlt |

  Falsche Argumente: `ArgumentException`-Familie.
- **Datenschutz:** Meldungen und `ToString()` aller DTOs enthalten nie Diktattext, Keys oder Header
  (Längen statt Inhalt). Audio liegt nur im Speicher (`AudioBuffer`).
- **Sprache:** `"auto"` oder ISO-Code. Die Prompt-Sprache folgt der Erkennungssprache: `de` → Deutsch, sonst Englisch.

## Interfaces

### `IDictationPipeline`
Zweck: der ganze Weg eines Diktats — Stille trimmen → Erkennung (`FallbackBackend`) → Verbesserung
(Online → Fallback-Zugang → lokal → Rohtext + Regeln) → Regeln (`ITextPolisher`).

| Methode | Verhalten |
|---|---|
| `Task<DictationResult> RunAsync(DictationRequest, CancellationToken)` | Wirft nur, wenn die **Erkennung** scheitert (`ApiNotConfigured`, `ApiHttp`, `ApiNetwork`, `LocalNotAvailable`) oder abgebrochen wird. Scheitert die Verbesserung, kommt ein Ergebnis mit Rohtext + Regeln und `RefinedText.Skipped`. |
| `Task<RefinedText> RefineAsync(string raw, string language, Refinement, CancellationToken)` | Neu verarbeiten aus dem Rohtext, ohne Audio. Wirft nur bei Abbruch. |

Fake: `WhisperLoom.Core.Testing.FakeDictationPipeline` (Rohtext einstellbar, Ergebnis `"[<stufe>] <roh>"`,
Fehler über `FakeFailure` — `SttHttp`, `SttNetwork`, `SttNotConfigured`, `SttLocalNotAvailable`, `Refine` —,
Verzögerung über `Delay`).

### `ITranscriptionBackend`
Zweck: Spracherkennung über einen Anbieter oder lokal (Port von `TranscriptionBackend`).

| Member | Verhalten |
|---|---|
| `string Label` | Anzeige ("OpenAI", "Lokal"); bei der Fallback-Kombination der zuletzt genutzte. |
| `Task<TranscriptResult> TranscribeAsync(AudioBuffer, string language, CancellationToken)` | 16 kHz Mono PCM16. Wirft `ApiNotConfigured`, `ApiHttp`, `ApiNetwork`, lokal (`ILocalStt`) `LocalNotAvailable`. Nach Abbruch kein zweiter Upload beim Fallback. |

Fallback-Regel (Phase 1, wie Android 3.9.1): scheitert der erste, erkennt der Fallback dieselbe Aufnahme und bleibt
für den Auftrag gesetzt; scheitern beide, geht der Fehler des ersten raus.

### `ITextRefiner`
Zweck: Textverbesserung über einen Online-Zugang (OpenAI-kompatibles `/chat/completions` oder native Ollama-API).

| Member | Verhalten |
|---|---|
| `string ModelLabel` | Modell für die Anzeige. |
| `Task<string> RefineAsync(string raw, string language, Refinement, CancellationToken)` | KI-Fassung ohne Regeln. Leer oder `RefineMode.Off` → Rohtext ohne Anfrage. Wirft `ApiNotConfigured`, `RefineRejected`, `ApiHttp` (bei 400 erst nach zweitem Versuch ohne `temperature`), `ApiNetwork`. |

### `IRefinePromptBuilder`
Zweck: System-Prompt je Stufe und Sprache zusammensetzen (Port von `RefinePrompt`; Fragmente als eingebettete
Ressourcen, Parität per `check_prompt_parity.py`). Synchron, rein.

| Methode | Verhalten |
|---|---|
| `string Build(RefineMode, bool german, bool smartFillers, bool paragraphs = true, bool isShort = false)` | `ArgumentException` bei `RefineMode.Off`. |
| `string UserText(string raw, bool german)` | Diktat als abgegrenzter Datenblock. |

### `ITextPolisher`
Zweck: Regeln ohne KI — Füllwörter, Großschreibung, Leerzeichen (Port von `TextPolisher`). Synchron, wirft nie.

| Methode | Verhalten |
|---|---|
| `string Polish(string raw, PolishOptions)` | Leer → leer. |
| `IReadOnlyList<string> BuiltinFillers(string language)` | Eingebaute Liste für die Einstellungen. |

### `ILocalStt : ITranscriptionBackend`
Zweck: lokale Erkennung über den whisper.cpp-Begleitprozess auf 127.0.0.1 (Phase 4).
`Task<bool> IsReadyAsync(CancellationToken)` prüft Modell und Startbarkeit, ohne zu starten; wirft nur bei Abbruch.
`TranscribeAsync` wirft `LocalNotAvailableException`, wenn Modell oder Prozess fehlen.

### `ILocalRefiner : ITextRefiner`
Zweck: lokale Textverbesserung über Ollama auf 127.0.0.1:11434 (Phase 4).
`Task<bool> IsReadyAsync(CancellationToken)` prüft Erreichbarkeit und Modell (`/api/tags`); wirft nur bei Abbruch.
`RefineAsync` wirft `LocalNotAvailableException`, wenn Ollama oder Modell fehlen.

### `ISecretStore`
Zweck: API-Keys außerhalb von `settings.json`. Windows: Credential Manager, Ziel `WhisperLoom:<name>`;
Linux: Secret Service (`secret-tool`); sonst `~/.config/whisperloom/secrets.json` mit Rechten 600 und Hinweis in der UI.
Tests nutzen das Präfix `WhisperLoom-Test-<id>:` und löschen ihre Einträge.

| Member | Verhalten |
|---|---|
| `string Kind` | "Credential Manager", "Secret Service", "Datei" — für die UI. |
| `Task<string?> GetAsync(name, ct)` | null = nichts gespeichert. |
| `Task SetAsync(name, value, ct)` | Ersetzt; `ArgumentException` bei unbekanntem Namen oder leerem Wert. |
| `Task DeleteAsync(name, ct)` | Nichts gespeichert ist kein Fehler. |

Namen: `SecretNames.Stt`, `SttFallback`, `Llm`, `LlmFallback`. Werte erscheinen nie in Logs, Exporten oder Meldungen.
**Abweichung vom Goal** (dort `WhisperLoom:<anbieter>`): je Zugang statt je Anbieter, wie die Key-Felder in
`Prefs.kt`. Grund: Erkennung, Verbesserung und die beiden Fallbacks können beim selben Anbieter verschiedene Keys
haben (z. B. eigener Server mit anderem Token); „wie Erkennung“ (`llm.providerId = same`) liest den Key `stt`.
Credential-Manager-Ziele heißen damit `WhisperLoom:stt`, `WhisperLoom:llm`, … (passt zu `WhisperLoom:*` in A2.2).

### `IHistoryStore`
Zweck: Verlauf nur als Text (Roh, Ergebnis, Stufe, Anbieter, Zeit), abschaltbar, Standard 50 Einträge.

| Methode | Verhalten |
|---|---|
| `Task AddAsync(HistoryEntry, ct)` | Schreibt nichts, wenn der Verlauf aus ist; kürzt auf die Höchstzahl (älteste zuerst). |
| `Task<IReadOnlyList<HistoryEntry>> ListAsync(ct)` | Neueste zuerst. |
| `Task<HistoryEntry?> GetAsync(id, ct)` | null = unbekannt. |
| `Task<bool> DeleteAsync(id, ct)` | false = gab es nicht. |
| `Task ClearAsync(ct)` | |

Ablagefehler: `IOException`.

## DTOs

| Typ | Inhalt |
|---|---|
| `AudioBuffer` | `ReadOnlyMemory<short> Samples`, `SampleRate` (16 000), `DurationMs`, `IsEmpty`. |
| `DictationRequest` | `Audio`, `Language`, `Refinement`. |
| `DictationResult` | `Raw`, `Language` (wirksam), `DurationMs`, `Result` (`RefinedText`), `SttLabel`, `UsedSttFallback`, `Text`. |
| `RefinedText` | `Refinement`, `Text`, `Model?`, `Skipped?`, `Note?`, `Refined` (= Model gesetzt). Port von `Refined`. |
| `TranscriptResult` | `Text`, `DetectedLanguage?`. |
| `ProviderInfo`, `ModelOption` | Katalogdaten eines Anbieters (Port der Daten von `Provider`/`ModelOption`), keine Keys. |
| `ApiStyle` | `OpenAi`, `Ollama`, `ElevenLabs`. |
| `PolishOptions` | Optionen von `ITextPolisher`. |
| `HistoryEntry` | Verlaufseintrag (siehe oben). |

## Stufen: `RefineMode` und `Refinement` (portiert)

- `RefineMode`: `Off`, `Polish`, `Beautify`, `Summarize`, `Readable` (nie wählbar), `Prompt`; Schlüssel über
  `RefineModes.Key()`/`FromKey()` (Unbekanntes → `Off`), `ModelStage()`, Listen `Settings`, `ModelStages`,
  `ParagraphStages`, `SettingsFor(promptEnabled)`.
- `RefineWay` (`Dictation`, `Share`), `PolishCleanup` (`Plain`, `Clean`, `Readable`), `SummarizeForm` (`Auto`, `Prose`)
  mit Schlüsseln über `RefineKeys`.
- `Refinement(Mode, SmartFillers, Paragraphs)` mit `Refinement.Of(way, stage, cleanup, paragraphs, form)` — 1:1 wie
  `Refinement.kt`, abgesichert durch `RefinementTest` (7 Fälle wie Kotlin, `docs/test-mapping-contracts.md`).

## Settings-Modell v1

`Contracts/Settings/WhisperLoomSettings.cs`, JSON über `SettingsJson` (camelCase, `schemaVersion: 1`).

- Felder und Standardwerte aus `Prefs.kt` (PREFS_VERSION 7): Sprache, Engine, Erkennung (Anbieter, URL, Modell,
  Zeitlimit, Kontext-Wörter, Datei), Erkennungs-Fallback, Textverbesserung (Anbieter oder `same`, Modell, Modell je
  Stufe), Verbesserungs-Fallback, Stufe des Diktats mit Bereinigung/Form/Absätzen, Share-Abschnitt (ohne Funktion,
  für den Sync), Nachbearbeitung (Füllwörter, Großschreibung, Leerzeichen), Lokal, Verlauf.
- Stufen, Bereinigung und Form stehen als Schlüssel; Unbekanntes gilt wie auf Android als Standard.
  Ausnahme `refine.stage = "paragraphs"` (Altwert der früheren Stufe „Absätze“, kann aus Android kommen): gilt als
  `polish`, wie `Prefs.refineMode`. `RefineModes.FromKey("paragraphs")` ergäbe `Off` — beim Lesen der Stufe beachten.
- **Keine Keys** im Modell — die liegen im `ISecretStore`.
- **Nicht übernommen** (Android-UI, auf Desktop ohne Bedeutung): `vocab_file_name`, `welcome_seen`, `overlay_skipped`,
  `setup_skip_*`, `tutorial_seen`, `float_x`/`float_y`, `agent_enabled`, `agent_tutorial_seen`, Legacy `agent_url`/`agent_token`.
  `stt.vocabularyFile` ist ein Dateipfad (Android: `content://`-URI) und wird **nicht** synchronisiert.
- Lesen: unbekannte Felder werden toleriert, fehlende bekommen ihren Standardwert, `null` für einen Abschnitt →
  `JsonException`, neueres Schema → `NotSupportedException`. Migration kommt mit dem ersten Schema-Wechsel.
- Speichern: unbekannte Felder der **obersten Ebene** (z. B. `android`) bleiben über `Extra` (`[JsonExtensionData]`)
  erhalten; unbekannte Felder **innerhalb** bekannter Abschnitte gehen verloren.
- Spätere Phasen ergänzen additiv (z. B. Desktop-Abschnitt mit Hotkeys, Ausgabe, Proxy; Policy für Provisioning).
