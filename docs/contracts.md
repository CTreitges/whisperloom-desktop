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
| `HistoryEntry` | Verlaufseintrag (siehe oben). W2-OUT, additiv: optional `Versions` (letzter Parameter, `null` = nur die damalige Fassung `Text`, so wie ältere Dateien; im JSON fehlt `versions` dann). `Raw` ist der Ursprung und wird nie überschrieben. W2-SEL, additiv: optional `Source` (letzter Parameter; `null` = Diktat, im JSON fehlt `source` dann; `HistoryEntry.SourceSelection` = `"selection"` für „Markierung verarbeiten“, `Raw` ist dann die Markierung). Android kennt dasselbe Feld (`bubble`/`keyboard`). |
| `HistoryVersion` | W2-OUT: eine Fassung (`Processing` = Stufen-Schlüssel oder `edited` für den bearbeiteten Ursprung, `Text`, `CreatedAt`, `Model`, `Failed`, `Edited`); je Eintrag höchstens eine Fassung je Schlüssel, Ersetzen am Platz. Bearbeiten und Neu verarbeiten schreiben den Eintrag per `AddAsync` unter derselben Id. |

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
  Umsetzung: `SyncImport.Preview` übernimmt das Feld nicht, `SyncExport` lässt es weg; nur das Provisioning setzt es.
  `VocabularySource` liest nur `.md`, `.markdown` und `.txt`.
- Lesen: unbekannte Felder werden toleriert, fehlende bekommen ihren Standardwert, `null` für einen Abschnitt →
  `JsonException`, neueres Schema → `NotSupportedException`. Migration kommt mit dem ersten Schema-Wechsel.
- Speichern: unbekannte Felder der **obersten Ebene** (z. B. `android`) bleiben über `Extra` (`[JsonExtensionData]`)
  erhalten; unbekannte Felder **innerhalb** bekannter Abschnitte gehen verloren.
- Spätere Phasen ergänzen additiv (z. B. Desktop-Abschnitt mit Hotkeys, Ausgabe, Proxy; Policy für Provisioning).

## Lauf 2 (W2-C0): Richtlinie mit drei Stufen, neue Abschnitte, Server-API

Stand 2026-11-01. Felder und Regeln im Detail: [settings-format.md](settings-format.md) („Richtlinie: drei Stufen“,
neue Felder), Client↔Server: [server-api.md](server-api.md).

| Typ / Member | Inhalt |
|---|---|
| `ManagedState` (`Free`, `Default`, `Fixed`), `ManagedStates.Key()`/`FromKey()` | Stufe je Pfad; JSON `"free"`/`"default"`/`"fixed"` (`FromKey`: unbekannt → null) |
| `PolicySettings.Defaults` | Vorgaben: Blatt-Pfad → Firmenwert (`JsonElement`); in `policy.json` als `defaults` |
| `PolicySettings.StateOf(path)` | Fest vor Vorgabe vor Frei, für Blätter und Abschnitte |
| `PolicySettings.IsLocked(path)` | jetzt ohne Groß-/Kleinschreibung; nie für Tastenkürzel und `management.*` außer `management.allowLeave` |
| `PolicySettings.FromProvisioning(policy, settings, warnings)` | neue Überladung: `lockSettings` + `levels`; die alte Überladung (nur `lockSettings`) bleibt |
| `ManagedDefaults.TrackOverrides / IsOverridden / Reset` | User-Override je Pfad (`management.overrides`) und „Auf Firmenvorgabe zurücksetzen“ |
| `ManagedPaths.IsAtOrBelow / IsHotkey / IsManagement / CanBeFixed / Find / Set` | Pfad-Regeln, alle ohne Groß-/Kleinschreibung |
| `WhisperLoomSettings.Output` (`OutputSettings`: `AlwaysCopy`, `PressEnter`) | Ausgabe |
| `WhisperLoomSettings.Selection` (`SelectionSettings`: `HotkeyCombo`, `FloatingButton`, `CtrlRightClick`) | Markierung verarbeiten |
| `WhisperLoomSettings.Providers` (`ProvidersSettings.Custom` → `CustomProvider`) | eigene Anbieter, Verweis `custom:<id>` |
| `OfflineSettings.CustomModels` (`CustomWhisperModel`), `OfflineSettings.CustomModelPrefix` | eigene Whisper-Dateien, `offline.model = "custom:<name>"` |
| `WhisperLoomSettings.Management` (`ManagementSettings`) | Firmen-Anbindung + `Overrides`; nie exportiert, nie importiert |
| `LocalServer` | Eintrag von `local.servers` (App-Abschnitt `local`, `LocalSettings.Servers`, dazu `TextEngine`, `BundledModel`) |
| `SecretNames.Device` (`"device"`) | Geräte-Token des Firmen-Servers; nicht in `SecretNames.All`, nie per Provisioning |
| App: `AppProfile.PressEnter` | `bool?`: null = Erben (`output.pressEnter`), true/false = An/Aus. Einzige Typänderung: `bool` → `bool?` liest alte Dateien unverändert; Aufrufer nutzen bis W2-OUT `?? false` |

### W2-LOCAL: Anbieter-Verzeichnis, Einträge mit eigenem Key (additiv)

| Typ / Member | Inhalt |
|---|---|
| `ProviderInfo.Family` (optional, `""`) | nur bei Einträgen außerhalb des Katalogs: Katalog-ID, deren Regeln gelten (`custom`, `anthropic`, `ollama`, `elevenlabs`). Gesetzt = feste Adresse; Fähigkeit über die Modell-Listen (`null` = kann es nicht, leer = Modelle vom Server) |
| `Core/Api/ProviderDirectory` + `IProviderSource` (`Prefix`, `Providers`) | löst `*.providerId` auf: Katalog, `custom:<id>` (`CustomProviderSource`), `local:<name>` (`LocalServerSource`); weitere Quelle per `With(source)` (W2-ENR: `company:`). Unbekannte ID mit Präfix → Anbieter ohne Adresse (nie OpenAI) |
| `ProviderDirectory.KeyOf(entry)`, `WithKeys(...)` | Key eines Eintrags: nur sein eigener, nie der des Zugangs, nie geerbt; lokale Server nie |
| `AccessResolver.Resolve*(…, providers)` | optionaler Parameter; Einträge nutzen nie Adresse oder Key des Zugangs |
| `SecretNames.ForProvider(id)`, `IsProviderKey(name)` | Key-Name eines eigenen Anbieters = Anbieter-ID `custom:<id>`; die Key-Ablagen akzeptieren ihn |
| `AccessKeys.Providers` | Keys der eigenen Anbieter (Name → Key); `SettingsAccess.KeysAsync(store, ct, settings)` lädt sie |
| `*.providerId = "local:<name>"` | Eintrag aus `local.servers` dieses Geräts (gerätebezogen; ein Import ohne passenden Server ist ein Fehler) |
| App-Abschnitt `local.textServer` | Name des Text-Servers bei `textEngine = "server"`, `""` = erster; gerätebezogen wie `local.servers` |
| `Core/Local/WhisperModels` | `offline.model` → Datei; `custom:<name>` fällt nie still auf Small zurück (Meldung bei fehlendem Eintrag, fehlender Datei, keiner ggml/gguf-Magic) |
| `Core/Local/TextModelCatalog`, `LocalServerRefiner` | mitgelieferte Textmodelle (Apache-2.0, SHA-256), lokaler OpenAI-kompatibler Text-Server |

Abweichungen von den Namensvorschlägen des Issues: `selection.hotkey` heißt `selection.hotkeyCombo` (kein Schema-Feld
darf auf `key` enden, Test `KeinKeyFeldImSchema`); `local.servers[].kind` (`stt`/`text`) nennt den Zweck.

### W2-SEL: Markierung verarbeiten (additiv)

| Typ / Member | Inhalt |
|---|---|
| `HistoryEntry.Source` | siehe oben; `"selection"` = Eintrag aus „Markierung verarbeiten“ |
| `Core/Output/OutputDecision.IsCautious(process, windowClass)` | „Vorsichtig“ einfügen: Browser (`IsBrowser`) oder Electron-Fenster (Klasse `Chrome_WidgetWin_1`, `ChromiumWindowClass`) – bei Fokus „unbekannt“ alte Zwischenablage zurück (Entscheidung Christof 2026-11-01) |
| App: `IWindowAdapter.WindowClass(hwnd)` | Klasse des Zielfensters; Default-Methode `""`, bestehende Implementierungen bleiben gültig |
| CLI/IPC `process-selection [--stage <stufe>]` | Windows: Markierung des Vordergrundfensters → Wähler bzw. sofort mit Stufe; Linux: Primärauswahl → Wähler-Fenster bzw. Stufe → Zwischenablage. Antwort `picker`, `done`, `instruction`, `empty`, `password`, `no-tool`, `stage-locked`, `stage-unavailable`, `busy`, `unavailable` |
| `status --json` | `input.selection` (`picker`, `pickerOpen`, `options`, `highlighted`, `button` [x, y], `buttonVisible`, `pickerRect`), `input.mouseHook`, `history.latest.source` |
| App-Abschnitt `desktop.profiles[].floatingButton` | `null` = wie `selection.floatingButton`, `false` = hier nie |
| `desktop.hotkeyRewrite` | entfällt; Migration nach `selection.hotkeyCombo` beim Laden (`SelectionMigration`) |
