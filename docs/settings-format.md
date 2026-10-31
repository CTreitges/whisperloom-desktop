# Einstellungsformat v1

Ein Format für drei Zwecke: `settings.json` im Datenordner, Export/Import zwischen WhisperLoom Desktop und der
Android-App, Provisioning-Dateien der IT. Beispiele: [`tests/fixtures/settings-v1.json`](../tests/fixtures/settings-v1.json)
(vollständig) und [`tests/fixtures/settings-v1-provisioning.json`](../tests/fixtures/settings-v1-provisioning.json)
(Provisioning mit Fake-Keys). Beide Dateien nutzen auch die Tests der Android-App (WLD-P5c).

Umsetzung Desktop: `src/WhisperLoom.Core/Settings/` (Lesen, Migration, Validierung, Import, Export, Provisioning,
Richtlinie), Modell `src/WhisperLoom.Core/Contracts/Settings/WhisperLoomSettings.cs`.

## Grundregeln

- JSON-Objekt, UTF-8, Feldnamen in camelCase. Kommentare und ein Komma am Ende sind beim Lesen erlaubt.
- `schemaVersion` (Zahl) gibt das Schema an. Fehlt das Feld, gilt die Datei als Schema 0 und wird migriert.
  Eine Datei mit höherer Version als bekannt wird abgelehnt („Bitte WhisperLoom aktualisieren“).
- **Teil-Dateien sind erlaubt.** Beim Import ersetzen die Felder der Datei die aktuellen Werte, fehlende Felder bleiben,
  wie sie sind. Objekte werden feldweise gemischt, Listen und einzelne Werte ganz ersetzt.
- **Keys stehen nie in Einstellungen oder Exporten.** Nur Provisioning-Dateien dürfen `secrets` enthalten.
- **Unbekannte Felder der obersten Ebene** (z. B. aus einer neueren Version) bleiben beim Import erhalten und werden
  wieder exportiert, ebenso unbekannte Felder in `desktop` und der ganze Abschnitt `android`. Unbekannte Felder
  innerhalb der übrigen Abschnitte (`stt`, `llm`, …) gehen verloren: neue Felder deshalb nur mit Standardwert und
  ohne Bedeutungswechsel einführen.
- Ein Import prüft die Werte streng (Tabelle unten). Bei einem Fehler wird nichts übernommen; die Meldung nennt
  den Pfad, z. B. `history.size: eine von 10, 25, 50, 100, 250, 500`.

## Felder

| Pfad | Typ | Standard | Bedeutung |
|---|---|---|---|
| `schemaVersion` | Zahl | `1` | Schema |
| `language` | Text | `"de"` | Erkennungssprache: `"auto"` oder ISO-Code (2–3 Kleinbuchstaben) |
| `engine` | Text/null | `null` | `"online"` oder `"offline"`; null = noch nicht gewählt |
| `stt.providerId` | Text | `"openai"` | Anbieter der Erkennung (ID aus `providers.json`, mit Erkennung) |
| `stt.baseUrl` | Text | `""` | `""` = Basis-URL des Anbieters, sonst `http(s)://…` |
| `stt.model` | Text | `""` | `""` = Empfehlung |
| `stt.readTimeoutSec` | Zahl | `0` | 0 = Standard, sonst 30–1800 |
| `stt.vocabulary` | Text | `""` | Kontext-Wörter, eins pro Zeile |
| `stt.vocabularyFile` | Text | `""` | Pfad einer Wortliste — gerätebezogen, Android ignoriert ihn |
| `sttFallback.enabled` / `.providerId` / `.baseUrl` / `.model` | | aus / `"openai"` / `""` / `""` | Ersatz-Erkennung |
| `llm.providerId` | Text | `"same"` | `"same"` = Zugang der Erkennung, sonst Anbieter mit Textmodell |
| `llm.baseUrl` / `llm.model` | Text | `""` | wie bei `stt` |
| `llm.stageModels.<stufe>` | Text | – | Modell je Stufe: `polish`, `beautify`, `summarize`, `prompt` |
| `llmFallback.enabled` / `.providerId` / `.baseUrl` / `.model` | | aus / `"openai"` / `""` / `""` | Ersatz-Textmodell |
| `refine.stage` | Text | `"off"` | Stufe beim Diktat: `off`, `polish`, `beautify`, `summarize`, `readable`, `prompt` (Altwert `paragraphs` = `polish`) |
| `refine.promptEnabled` | bool | `false` | Stufe „Prompt“ anbieten |
| `refine.polishCleanup` | Text | `"plain"` | `plain`, `clean`, `readable` |
| `refine.summarizeForm` | Text | `"auto"` | `auto`, `prose` |
| `refine.paragraphsPolish` / `paragraphsBeautify` | bool | `true` | Absätze je Stufe |
| `share.stage` / `.polishCleanup` / `.summarizeForm` / `.hideFillers` | | `"off"` / `"plain"` / `"auto"` / `true` | Weg „Teilen“ (Android); Desktop speichert ihn nur |
| `polish.removeFillers` / `autoCapitalize` / `trailingSpace` | bool | `true` | Text-Regeln |
| `polish.customFillers` | Liste | `[]` | eigene Füllwörter, klein, ohne Duplikate |
| `polish.disabledFillers` | Liste | `[]` | abgeschaltete Wörter der eingebauten Liste |
| `offline.model` / `.accurate` / `.refine` / `.localLlmModel` | | `"small"` / `true` / `"local"` / `"gemma4_e2b"` | lokale Erkennung; `refine`: `local`, `online_local`, `skip` |
| `history.enabled` / `history.size` | bool / Zahl | `true` / `50` | Verlauf; Größe: 10, 25, 50, 100, 250, 500 |
| `serverModelsEnabled` | bool | `false` | Modellliste des Anbieters statt Katalog |

## Abschnitte je App

| Abschnitt | Gehört | Andere App |
|---|---|---|
| `desktop` | Desktop: `uiLanguage` (`""`/`de`/`en`), `output`, `microphone` (gerätebezogen), `stageFontSize`, `proxyMode` (`system`/`off`/`manual`), `proxyUrl`; später Hotkeys und App-Profile (P3, P5b) | Android liest ihn nicht, speichert ihn beim Import und gibt ihn beim Export unverändert zurück |
| `android` | Android-eigene Felder (Inhalt bestimmt die Android-App) | Desktop liest ihn nicht, zeigt ihn nicht in der Vorschau, prüft ihn nicht und gibt ihn unverändert weiter |

## Provisioning-Zusatz

Nur in Dateien für `WhisperLoom provision <datei>` (siehe [provisioning.md](provisioning.md)). Ein normaler Import
(Knopf in den Einstellungen, `WhisperLoom import`) ignoriert beide Abschnitte mit Hinweis. Ein Export enthält sie nie.

### `secrets`

Objekt Name → Key. Der Name ist

- eine **Anbieter-ID** (`"groq"`, `"openai"`, …): Der Key gilt für jeden Zugang, der nach dem Provisioning diesen
  Anbieter nutzt (Erkennung, Ersatz-Erkennung, Textmodell, Ersatz-Textmodell). `llm.providerId = "same"` braucht
  keinen eigenen Key. Nutzt kein Zugang den Anbieter, gibt es eine Warnung.
- oder ein **Zugangsname** `stt`, `stt_fallback`, `llm`, `llm_fallback` — hat Vorrang vor der Anbieter-ID, nötig
  z. B. für zwei eigene Server (`custom`) mit verschiedenen Keys.

Die Keys landen im Credential Manager (Windows, Ziel `WhisperLoom:<zugang>`), im Secret Service bzw. einer Datei mit
Rechten 600 (Linux) — nie in `settings.json`, nie in Logs oder Ausgaben.

### `policy`

| Feld | Typ | Wirkung |
|---|---|---|
| `lockSettings` | `true` / `false` / Liste | `true` sperrt alle Felder, die die Datei setzt, plus die gesetzten Keys (`secrets.<zugang>`); eine Liste sperrt genau diese Pfade (ein Abschnitt wie `"llm"` sperrt alles darunter); `false` nichts |
| `historyEnabled` | bool | Verlauf fest an bzw. aus; der Schalter ist gesperrt |
| `updatesEnabled` | bool | automatische Updates (`false` für IT-Verteilung); wertet die Update-Funktion aus |
| `allowedProviders` | Liste | nur diese Anbieter-IDs sind wählbar; Importe mit anderen Anbietern werden abgelehnt (abgeschaltete Fallbacks zählen nicht) |

Gesperrte Felder sind im Einstellungsfenster ausgegraut („Ausgegraute Felder sind von IT festgelegt“), ein Import des
Nutzers lässt sie stehen (die Vorschau zeigt sie als „von IT festgelegt“), eine gesperrte Stufe ändern weder Hotkey
noch Tray noch `WhisperLoom stage`. Ein neues Provisioning ersetzt die Richtlinie vollständig.

Gespeichert wird die Richtlinie getrennt in `<Datenordner>/policy.json` (Felder `locked` als aufgelöste Pfadliste,
`historyEnabled`, `updatesEnabled`, `allowedProviders`); `status --json` zeigt sie unter `policy`.

## Migration

| Von | Nach | Schritt |
|---|---|---|
| 0 (ohne `schemaVersion`) | 1 | `refine.stage`/`share.stage` = `paragraphs` → `polish`; Füllwörter klein, getrimmt, ohne Duplikate; `schemaVersion: 1` |

Ein neues Schema bekommt einen weiteren Schritt in `SyncMigration`; die Tabelle hier wird mit erweitert.

## Befehle

```
WhisperLoom export <datei>              Einstellungen ohne Keys exportieren
WhisperLoom import <datei> [--dry-run]  Vorschau („change pfad: alt -> neu“, „locked …“) und übernehmen
WhisperLoom provision <datei>           Keys, Einstellungen, Richtlinie (IT)
```

Eine laufende Instanz lädt nach `import` und `provision` neu. Exit-Codes: 0 ok, 1 Datei ungültig, 2 Aufruf falsch.
