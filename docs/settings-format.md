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
- **Abgeschaltete OpenAI-Modelle** (TD-A4, wie Android `PrefsMigration` v8): Beim Laden und beim Lesen jeder Import-,
  Provisioning- oder Server-Datei gehen `stt.model`, `sttFallback.model`, `llm.model`, `llm.stageModels.*` und
  `llmFallback.model` mit Anbieter `openai` von `gpt-5-mini` → `gpt-5.6-terra`, `gpt-5-nano` → `gpt-5.6-luna`,
  `gpt-4o-transcribe`/`gpt-4o-mini-transcribe`/`whisper-1` → `gpt-transcribe` (auch datierte Snapshots). Idempotent, ohne
  neue `schemaVersion`; andere Anbieter und eigene Server bleiben. Ein fest gesetzter (Fest) Wert geht ebenfalls auf den
  Nachfolger, mit Warnung bzw. Log-Eintrag `management.retired_model` (nur Pfad).
- **Teil-Dateien sind erlaubt.** Beim Import ersetzen die Felder der Datei die aktuellen Werte, fehlende Felder bleiben,
  wie sie sind. Objekte werden feldweise gemischt, Listen und einzelne Werte ganz ersetzt.
- **Keys stehen nie in Einstellungen oder Exporten.** Nur Provisioning-Dateien dürfen `secrets` enthalten.
- **Unbekannte Felder der obersten Ebene** (z. B. aus einer neueren Version) bleiben beim Import erhalten und werden
  wieder exportiert, ebenso unbekannte Felder in `desktop` und der ganze Abschnitt `android`. Unbekannte Felder
  innerhalb der übrigen Abschnitte (`stt`, `llm`, …) gehen verloren: neue Felder deshalb nur mit Standardwert und
  ohne Bedeutungswechsel einführen.
- Ein Import prüft die Werte streng (Tabelle unten). Bei einem Fehler wird nichts übernommen; die Meldung nennt
  den Pfad, z. B. `history.size: eine von 10, 25, 50, 100, 250, 500`.
- **Fremde Dateien lenken keinen Key um.** Ändert ein Import `*.baseUrl`, `*.providerId` oder einen manuellen Proxy
  (`desktop.proxyMode`/`proxyUrl`) so, dass ein gespeicherter Key an ein neues Ziel ginge (anderer Host, auch über
  einen Fallback oder die Textverbesserung, die den Key erben; abgeschaltete Fallbacks zählen mit), zeigt die Vorschau
  die Zeile `secrets.<zugang>: gespeichert → gelöscht (neues Ziel …)` mit Warnung. Beim Übernehmen wird dieser Key
  gelöscht, bevor die Einstellungen gespeichert werden; nur neu eingeben, wenn man dem neuen Ziel vertraut. Bleibt der
  Host gleich, bleibt der Key.
  Bei `llm.providerId = "same"` zählt für den `llm`-Key die Adresse der Erkennung (dorthin schickt ihn „Zugang prüfen“).
  Felder in `desktop` und `local` zählen in jeder Groß-/Kleinschreibung, wie die App sie liest.
- **Neue Ziele für Diktattext** nennt die Vorschau auch ohne Key („Diktate gehen künftig auch an …“): Erkennung,
  Textverbesserung und eingeschaltete Fallbacks. Die Antwort eines Textmodells wird eingefügt bzw. getippt.
- **Gerätebezogene Ziele** übernimmt ein Import nie: `stt.vocabularyFile` und `local.ollamaUrl` (Adresse der lokalen
  Textverbesserung). Ein Export enthält `stt.vocabularyFile` nicht.
- **W2, gerätebezogen:** `offline.customModels` (Dateipfade), `local.servers` (Ziele für Diktattext) und `local.textServer` übernimmt kein
  Import, kein Export enthält sie; nur das Provisioning setzt sie.
- **W2, `management`** (Firmen-Anbindung, Gerätezustand) übernimmt kein Import (Warnung „management … ignoriert“), kein
  Export enthält ihn, auch nicht in anderer Schreibweise (`Management`).
- **W2, `output.pressEnter`:** `true` übernimmt ein Import nur, wenn es schon `true` war; sonst kommt `false` an
  (`ImportPreview.EnterDisabled` enthält dann `output.pressEnter`), wie bei App-Profilen.

## Felder

| Pfad | Typ | Standard | Bedeutung |
|---|---|---|---|
| `schemaVersion` | Zahl | `1` | Schema |
| `language` | Text | `"de"` | Erkennungssprache: `"auto"`, ISO-Code (2–3 Kleinbuchstaben) oder `"de+en"` (Deutsch + Englisch, CTR-273/W2-PFIX: gpt-transcribe bekommt `languages[]=de` und `=en`, alle anderen Erkenner erkennen selbst; wirksam ist die erkannte Sprache, sonst Deutsch) |
| `engine` | Text/null | `null` | `"online"` oder `"offline"`; null = noch nicht gewählt |
| `stt.providerId` | Text | `"openai"` | Anbieter der Erkennung (ID aus `providers.json`, mit Erkennung) |
| `stt.baseUrl` | Text | `""` | `""` = Basis-URL des Anbieters, sonst `https://…`; `http://` nur bei Anbietern, die es erlauben (eigener Server, Ollama), und nur zu privaten Adressen (LAN, VPN, Tailscale) |
| `stt.model` | Text | `""` | `""` = Empfehlung |
| `stt.readTimeoutSec` | Zahl | `0` | 0 = Standard, sonst 30–1800 |
| `stt.vocabulary` | Text | `""` | Kontext-Wörter, eins pro Zeile |
| `stt.vocabularyFile` | Text | `""` | Pfad einer Wortliste (`.md`, `.markdown`, `.txt`; andere Dateien liest die App nicht) — gerätebezogen: Import und Export lassen ihn weg, nur das Provisioning setzt ihn; Android ignoriert ihn |
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
| `offline.model` (W2) | Text | `"small"` | zusätzlich `"custom:<name>"` = Eintrag aus `offline.customModels`; unbekannter Name ist ein Fehler |
| `offline.customModels` (W2) | Liste | `[]` | eigene Whisper-Dateien `{name, path}`; `name` eindeutig, nicht leer — gerätebezogen |
| `output.alwaysCopy` (W2) | bool | `false` | Text zusätzlich immer in die Zwischenablage |
| `output.pressEnter` (W2) | bool | `false` | Nach dem Einfügen Enter senden; ersetzt die Strg+Enter-Taste; Profile mit `pressEnter: null` erben es |
| `selection.hotkeyCombo` (W2) | Text | `"Ctrl+Alt+Shift+S"` | Kombi „Markierten Text verarbeiten“, `""` = aus. Name statt `hotkey`, weil kein Schema-Feld auf `key` enden darf. Migration (W2-SEL): eine gespeicherte `desktop.hotkeyRewrite` wird beim Laden hierher übernommen, solange hier noch der Standard steht; leer/ungültig galt als Standard |
| `selection.floatingButton` (W2) | bool | `false` | schwebender Knopf an der Markierung |
| `selection.ctrlRightClick` (W2) | bool | `false` | Strg+Rechtsklick auf die Markierung |
| `providers.custom` (W2) | Liste | `[]` | eigene Anbieter, siehe unten |
| `*.providerId` (W2-LOCAL) | Text | | zusätzlich `"custom:<id>"` (eigener Anbieter) und `"local:<name>"` (Eintrag aus `local.servers` dieses Geräts); ohne passenden Eintrag bzw. ohne passende Fähigkeit ein Fehler |
| `management.*` (W2) | | | Firmen-Anbindung, siehe unten; nie exportiert, nie importiert |

### Eigene Anbieter (`providers.custom`, W2)

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `id` | Text | `""` | `[a-z0-9][a-z0-9_-]{0,31}`, eindeutig. Ein Zugang nennt den Eintrag mit `*.providerId = "custom:<id>"` |
| `name` | Text | `""` | Anzeige, nicht leer |
| `protocol` | Text | `"openai"` | `openai` (Erkennung + Text), `ollama` (Text), `elevenlabs` (Erkennung), `anthropic` (Text); ein Zugang ohne passende Fähigkeit ist ein Fehler |
| `baseUrl` | Text | `""` | `https://…`, `http://` nur zu privaten Adressen |
| `models` | Liste | `[]` | angebotene Modell-IDs; leer = Liste vom Server |
| `keyOptional` | bool | `false` | Zugang ohne Key erlaubt |

Keys der eigenen Anbieter liegen wie alle Keys im Secret-Store, aber **beim Eintrag**: Name = Anbieter-ID
`custom:<id>` (Windows `WhisperLoom:custom:<id>`). Ein Zugang mit `custom:<id>` nutzt immer Adresse und Key des
Eintrags, nie `*.baseUrl` oder den Key des Zugangs, und erbt keinen Key (W2-LOCAL). Lokale Server (`local:<name>`)
bekommen nie einen Key. Import: Ändert die Datei die Adresse eines Eintrags auf einen anderen Host, wird sein
gespeicherter Key wie bei K1 gelöscht (Warnung in der Vorschau). Provisioning: `secrets."custom:<id>"` speichert den
Key des Eintrags; ändert eine Provisioning-Datei (bzw. die Server-Konfiguration) die Adresse eines Eintrags oder entfernt ihn, ohne `secrets."custom:<id>"` mitzuliefern, wird der gespeicherte Key gelöscht (Warnung). Entfernt ein Import einen Eintrag, wird sein Key ebenfalls gelöscht. `allowedProviders`: `"custom"` gibt alle eigenen Anbieter frei, `"local"` alle lokalen Server,
einzelne Einträge auch direkt (`"custom:<id>"`, `"local:<name>"`).

### Firmen-Anbindung (`management`, W2)

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `serverUrl` | Text | `""` | Firmen-Server ohne `/api/v1` ([server-api.md](server-api.md)); `""` = keiner |
| `deviceId` | Text | `""` | Geräte-ID vom Server (`d_…`) |
| `tenantName` | Text | `""` | Firmenname für „Einige Einstellungen verwaltet <Firma>“ |
| `state` | Text | `"none"` | `none`, `pending`, `approved`, `blocked`, `revoked` |
| `lastSync` | Zahl | `0` | letzter Abgleich, ms seit 1970 (UTC) |
| `keyMode` | Text | `"proxy"` | `proxy` oder `distribute` (vom Server) |
| `allowLeave` | bool | `true` | Nutzer darf die Anbindung lösen; einziges `management`-Feld, das Fest sein darf |
| `code` | Text | `""` | Firmencode, nur als Eingabe einer Provisioning-Datei; die App verwirft ihn nach dem Enrollment |
| `overrides` | Liste | `[]` | Vorgabe-Pfade, die der Nutzer geändert hat (siehe Richtlinie) |

Eine Provisioning-Datei bzw. die Server-Antwort darf nur `serverUrl`, `code` und `allowLeave` setzen; die übrigen
Felder sind Gerätezustand (Warnung, ignoriert). Das Geräte-Token liegt im Secret-Store unter `device`, nie hier und nie
in `secrets` einer Datei (Fehler).

## Abschnitte je App

| Abschnitt | Gehört | Andere App |
|---|---|---|
| `desktop` | Desktop: `uiLanguage` (`""`/`de`/`en`), `output`, `microphone` (gerätebezogen), `stageFontSize`, `proxyMode` (`system`/`off`/`manual`), `proxyUrl` (ein manueller Proxy bekommt nie die Windows-Anmeldung NTLM/Kerberos, nur der System-Proxy); Windows: `hotkeysEnabled`, `hotkeyHold`, `hotkeyToggle`, `hotkeyStage`, `hotkeyReinsert` (`Ctrl+Alt+Shift+V`), `hotkeyRewrite` (bis W2-SEL; wird beim Laden nach `selection.hotkeyCombo` migriert und entfernt), `insertMode` (`paste`/`type`/`clipboard`; `type` in Konsolen und Editoren mit Terminal tippt Zeilenumbrüche als Leerzeichen, sonst führte Umschalt+Enter die Zeile aus), `profiles` (App-Profile, siehe unten) | Android liest ihn nicht, speichert ihn beim Import und gibt ihn beim Export unverändert zurück |
| `local` | Desktop (`LocalSettings`): `ollamaModel`, `ollamaUrl` (gerätebezogen), `backend` (`auto`/`cpu`), `idleMinutes`; W2: `textEngine` (`ollama` Standard, `bundled`, `server`), `bundledModel` (ID, `""` = Empfehlung), `servers` (Liste `{name, kind: stt\|text, baseUrl, model}`, gerätebezogen), W2-LOCAL: `textServer` (Name des Text-Servers bei `textEngine = "server"`, `""` = erster; gerätebezogen) | Android liest ihn nicht |
| `android` | Android-eigene Felder (Inhalt bestimmt die Android-App) | Desktop liest ihn nicht, zeigt ihn nicht in der Vorschau, prüft ihn nicht und gibt ihn unverändert weiter |

### App-Profile (`desktop.profiles`, Windows)

Liste von Objekten; gilt für Diktate per Tastenkürzel ins Zielfenster.

| Feld | Typ | Standard | Bedeutung |
|---|---|---|---|
| `name` | Text | `""` | Anzeige im Hinweis „Profil · Stufe“; `""` = Prozessname |
| `process` | Text | `""` | Prozessname mit oder ohne `.exe`, Groß-/Kleinschreibung egal (`OUTLOOK.EXE`); leer = Profil greift nie |
| `titlePattern` | Text | `""` | Regex auf den Fenstertitel (ohne Groß-/Kleinschreibung); `""` = jeder Titel; ungültig = Profil greift nie |
| `stage` | Text | `""` | Stufen-Schlüssel wie `refine.stage`; `""` = Standard-Stufe. Eine von IT gesperrte Stufe geht vor |
| `insertMode` | Text | `""` | `paste`/`type`/`clipboard`; `""` = `desktop.insertMode` |
| `pressEnter` | bool/null | `null` (W2; früher `false`) | Erben/An/Aus: `null` = `output.pressEnter`, `true` = An, `false` = Aus (ältere Dateien mit `false` bleiben „Aus“, gleiches Verhalten). Erben wird nicht geschrieben (Feld fehlt), weil ältere Versionen bei `null` den ganzen `desktop`-Abschnitt verwürfen. Ein Import macht ein bestehendes Profil nur sicherer: „Aus“ bleibt „Aus“, auch wenn die Datei Erben angibt. Nach dem Einfügen Enter senden. Ein Import übernimmt `true` nur für ein Profil, das es so schon gab; sonst kommt das Profil mit `false` an (Warnung in der Vorschau) und Enter gilt erst, wenn der Nutzer es im Profil selbst einschaltet |
| `floatingButton` | bool/null | `null` (W2-SEL) | Schwebender Knopf an einer Markierung in diesem Programm: `null` = wie `selection.floatingButton`, `false` = hier nie. `null` wird nicht geschrieben |
| `enabled` | bool | `true` | Profil aktiv |

Vorrang: ein Profil mit passendem `titlePattern` vor einem Profil nur mit `process`; unter gleich genauen das erste der
Liste. Der Stufen-Hotkey ändert die Stufe des passenden Profils (Vordergrundfenster bzw. Ziel des laufenden Diktats) und
speichert sie hier; ohne passendes Profil ändert er `refine.stage`.

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
| `allowedProviders` | Liste | nur diese Anbieter-IDs sind wählbar; Importe mit anderen Anbietern werden abgelehnt (abgeschaltete Fallbacks zählen nicht). Bei Anbietern mit fester Adresse ist auch eine `baseUrl` zu einem anderen Host ein Verstoß (Import und Einstellungsfenster); `custom` und `ollama` haben keine feste Adresse |

Gesperrte Felder sind im Einstellungsfenster ausgegraut („Ausgegraute Felder sind von IT festgelegt“), ein Import des
Nutzers lässt sie stehen (die Vorschau zeigt sie als „von IT festgelegt“), eine gesperrte Stufe ändern weder Hotkey
noch Tray noch `WhisperLoom stage`, gesperrte Keys nicht `WhisperLoom key set|delete` (Exit 1; ebenso bei unlesbarer
`policy.json`). Ersetzt ein Import einen
Abschnitt mit gesperrten Feldern durch einen Nicht-Objekt-Wert (z. B. `"desktop": 1`), bleibt der Abschnitt stehen.
Ein neues Provisioning ersetzt die Richtlinie vollständig. Die Sperren sind keine Sicherheitsgrenze (siehe
[provisioning.md](provisioning.md)).

Gespeichert wird die Richtlinie getrennt in `<Datenordner>/policy.json` (Felder `locked` als aufgelöste Pfadliste,
`historyEnabled`, `updatesEnabled`, `allowedProviders`; W2: `defaults` = Blatt-Pfad → Firmenwert); `status --json`
zeigt sie unter `policy`. W2-ENR: Daneben `<Datenordner>/policy-fixed.json` (Blatt-Pfad → Wert der festen Pfade zum
Zeitpunkt von Provisioning bzw. Abgleich); Start und Neuladen setzen diese Werte wieder ein, solange der Pfad Fest ist
(lokal geänderte `settings.json`). `management-cache.json` hält ETag, Proxy-Adresse und die letzte Server-Konfiguration
ohne `secrets`. Pfade der Sperre vergleichen ohne Rücksicht auf Groß-/Kleinschreibung (W2-C0: vorher umging
`desktop.ProxyMode` die Sperre `desktop.proxyMode`).

## Richtlinie: drei Stufen

Quelle ist eine Provisioning-Datei oder der Firmen-Server (`GET /api/v1/config`, [server-api.md](server-api.md)); beide
liefern dasselbe Modell (`settings` + `policy`) und laufen durch `Provisioning.Plan`.

| Stufe | `levels` | Wirkung |
|---|---|---|
| **Fest** | `"fixed"` | Wert aus der Datei (oder der aktuelle, wenn die Datei keinen hat), in der App ausgeblendet; die Seite zeigt nur „Einige Einstellungen verwaltet <Firma>“. Import, Hotkey, Tray, CLI ändern ihn nicht |
| **Vorgabe** | `"default"` | Wert aus der Datei (Pflicht), der Nutzer darf ändern. Seine Änderung gewinnt (`management.overrides`), bis „Auf Firmenvorgabe zurücksetzen“ |
| **Frei** | `"free"` | nichts zentral; ein Wert der Datei wird einmalig gesetzt wie bisher |

```json
{
  "refine": {"stage": "polish"},
  "desktop": {"proxyMode": "system", "hotkeyHold": "Ctrl+Win"},
  "policy": {
    "lockSettings": true,
    "levels": {"refine.stage": "default", "desktop.hotkeyHold": "default", "polish": "free"}
  }
}
```

- `policy.levels`: Objekt Pfad → Stufe; ein Abschnitt (`"llm"`) gilt für alles darunter. Unbekannter Pfad, unbekannte
  Stufe, Vorgabe ohne Wert in der Datei → Fehler, nichts wird übernommen.
- **`lockSettings` bleibt gültig** und bedeutet Fest (`true` = alle Pfade der Datei, Liste = genau diese). Ein Eintrag in
  `levels` geht `lockSettings` für denselben Pfad und alles darunter vor (im Beispiel: alles Fest außer
  `refine.stage`, `desktop.hotkeyHold` und `polish`).
- **Pfade ohne Stufe** sind Frei. Ein Server-Abgleich ohne Eintrag für einen früher festen Pfad gibt ihn frei; der Wert bleibt.
- **Tastenkürzel sind nie Fest** (jeder Pfad-Teil, der mit `hotkey` beginnt: `desktop.hotkey*`, `desktop.hotkeysEnabled`,
  `selection.hotkeyCombo`): gewünscht Fest → Vorgabe mit Warnung; unter einem festen Abschnitt (`"desktop"`) ebenso
  Vorgabe. Auch eine ältere `policy.json` mit gesperrtem Tastenkürzel sperrt es nicht mehr.
- **`management.*` ist nie Fest und nie Vorgabe** (Warnung, ignoriert), außer `management.allowLeave`, das Fest sein darf.
  `secrets.<zugang>` kann Fest, aber keine Vorgabe sein.
- **User-Override:** Ein Vorgabe-Pfad, dessen Wert von der Vorgabe abweicht, steht in `management.overrides`
  (`ManagedDefaults.TrackOverrides`, nach eigener Änderung und Import). Eine neue Vorgabe setzt nur Pfade ohne Override.
  „Auf Firmenvorgabe zurücksetzen“ (`ManagedDefaults.Reset`, ein Pfad oder alle) setzt den Firmenwert und entfernt den
  Override. Ändert der Nutzer auf genau den Firmenwert zurück, ist es kein Override mehr.

## Migration

| Von | Nach | Schritt |
|---|---|---|
| 0 (ohne `schemaVersion`) | 1 | `refine.stage`/`share.stage` = `paragraphs` → `polish`; Füllwörter klein, getrimmt, ohne Duplikate; `schemaVersion: 1` |

Ein neues Schema bekommt einen weiteren Schritt in `SyncMigration`; die Tabelle hier wird mit erweitert.

## Befehle

```
WhisperLoom export <datei>              Einstellungen ohne Keys exportieren
WhisperLoom import <datei> [--dry-run]  Vorschau („change pfad: alt -> neu“, „locked …“,
                                        „change secrets.<zugang>: stored -> deleted (<ziel>)“) und übernehmen
WhisperLoom provision <datei>           Keys, Einstellungen, Richtlinie (IT)
```

Eine laufende Instanz lädt nach `import` und `provision` neu. Exit-Codes: 0 ok, 1 Datei ungültig, 2 Aufruf falsch.
