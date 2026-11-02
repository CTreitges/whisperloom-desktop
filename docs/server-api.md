# Server-API v1 (Client ↔ Firmen-Server)

Stand 2026-11-01 (W2-C0). Vertrag zwischen WhisperLoom (Desktop, später Android) und dem selbst hostbaren
Firmen-Server unter `server/` (W2-SRV1, Proxy W2-SRV2). Änderungen nur additiv; Bedeutungswechsel nur mit `/api/v2`.
Begriffe „Fest / Vorgabe / Frei“ und das Richtlinien-Modell: [settings-format.md](settings-format.md#richtlinie-drei-stufen).

## 1. Gemeinsame Regeln

- **Basis:** `<serverUrl>/api/v1/…`. `serverUrl` ist `https://…` (http nur zu privaten Adressen wie bei `stt.baseUrl`),
  ohne abschließenden `/`; die App speichert sie in `management.serverUrl`.
- **Format:** JSON, UTF-8, Feldnamen in camelCase (Englisch). Unbekannte Felder in Antworten ignoriert der Client
  (additive Erweiterung). Unbekannte Felder in Anfragen lehnt der Server mit `400 invalid_request` ab.
- **Zeit:** Millisekunden seit 1970 (UTC) als Ganzzahl.
- **Keine Desktop-Annahmen:** `platform` ist ein freier Bezeichner (`windows`, `linux`, `android`, …); kein Feld setzt
  Windows-Pfade, Registry oder Credential Manager voraus. Wohin die App Token und Keys legt, entscheidet die Plattform
  (Desktop: `ISecretStore`, Android: Keystore).
- **Client-Kennung:** `User-Agent: WhisperLoom/<version> (<platform>)` bei jeder Anfrage.
- **Grenzen:** Request-Body höchstens 64 KiB (Proxy-Audio und Proxy-Text: siehe §3.7). Rate-Limits je IP, Code und Gerät; bei
  Überschreitung `429 rate_limited` mit `Retry-After: <Sekunden>`.
- **Datenschutz:** Der Server speichert Token und Firmencodes nur gehasht (SHA-256), Keys nur verschlüsselt. Logs nur
  Metadaten (Gerät, Route, Status, Dauer, Fehlerklasse), nie Diktattext, Audio, Keys, Token oder Header.

### 1.1 Anmeldung des Geräts

`Authorization: Bearer wld_<43 Zeichen base64url>` (Geräte-Token, 256 bit, aus §3.1). Die App legt es im Secret-Store
unter dem Namen `device` ab (`SecretNames.Device`), nie in `settings.json`. Fehlt der Header oder ist das Token unbekannt
bzw. widerrufen → `401 device_unknown`.

### 1.2 Fehlerformat

```json
{"error": "device_pending", "message": "Das Gerät wartet noch auf die Freigabe durch die IT."}
```

Die App wählt ihren Text nach `error` (DE/EN aus `Localization`); `message` ist Rückfallebene für unbekannte Codes.

| `error` | HTTP | Bedeutung | Verhalten der App |
|---|---|---|---|
| `invalid_request` | 400 | Format, Pflichtfeld, unbekanntes Feld | Fehler anzeigen, nicht wiederholen |
| `device_unknown` | 401 | Token unbekannt oder widerrufen (Gerät gelöscht) | `management.state = "revoked"`, Token löschen, „Neu verbinden“ anbieten; Firmen-Werte bleiben bis dahin stehen |
| `code_invalid` | 404 | Firmencode unbekannt oder abgelaufen | Eingabe prüfen lassen |
| `not_found` | 404 | Route unbekannt, z. B. Proxy-Routen (§3.6/§3.7) auf einem Server ohne Proxy; ebenso Proxy bei `keyMode: "distribute"` oder ohne passenden Zugang der Firma | nicht wiederholen; die Funktion gilt als vom Server nicht angeboten |
| `device_pending` | 403 | Gerät noch nicht freigegeben (Route braucht Freigabe) | Warten, `GET /device` abfragen |
| `device_blocked` | 403 | Gerät gesperrt | `management.state = "blocked"`, Proxy-Zugang aus |
| `tenant_blocked` | 403 | Firma gesperrt | wie `device_blocked` |
| `leave_not_allowed` | 403 | Abmelden ist von der Firma verboten | „Abmelden“ ausblenden |
| `budget_exhausted` | 403 | Proxy: Budget des Geräts bzw. der Gruppe aufgebraucht (auch: Budget der Firma; Monatsdeckel, §3.7) | Der Proxy lehnt ab, kein Ersatz-Anbieter; die App zeigt „Firmenbudget für diesen Monat erreicht – bitte IT fragen“ |
| `too_many_pending` | 409 | Zu viele wartende Geräte für diesen Code | später erneut |
| `payload_too_large` | 413 | Body zu groß | nicht wiederholen |
| `client_outdated` | 426 | App-Version zu alt; Feld `minAppVersion` | Update anbieten |
| `rate_limited` | 429 | Zu viele Anfragen; Header `Retry-After` | nach `Retry-After` wiederholen |
| `internal_error` | 500 | Serverfehler | später erneut |
| `upstream_error` | 502 | Proxy: Anbieter antwortete mit Fehler | wie `ApiHttpException`, Fallback greift |
| `service_unavailable` | 503 | Wartung | später erneut, `Retry-After` falls gesetzt |
| `upstream_timeout` | 504 | Proxy: Anbieter-Zeitlimit | wie `ApiNetworkException` |

Netzwerkfehler (DNS, TLS, Zeitlimit) sind keine Server-Antwort: Die App behält die zuletzt abgeglichene Konfiguration
und versucht es beim nächsten Intervall erneut.

### 1.3 Formate

- **Firmencode:** 16 Zeichen Crockford-Base32 (`0-9 A-H J K M N P-T V-Z`), angezeigt `ABCD-EFGH-JKMN-PQRS` (80 bit).
  Eingabe ohne Rücksicht auf Bindestriche, Leerzeichen, Groß-/Kleinschreibung; `O→0`, `I/L→1`. Der Code nennt die
  Firma (Mandant) und optional eine Gruppe; er ist mehrfach nutzbar, bis die IT ihn zurückzieht.
- **Geräte-ID:** `d_` + 12 Zeichen base32, kein Geheimnis (Anzeige, Portal-URLs).
- **Gerätezustand** (`state`, = `management.state` der App): `pending` | `approved` | `blocked`. Nur in der App
  zusätzlich: `none` (nicht verbunden) und `revoked` (nach `401 device_unknown`).
- **Key-Modus** (`keyMode`, je Firma): `proxy` (Standard, Keys bleiben auf dem Server) | `distribute` (Keys gehen an
  das Gerät).

## 2. Ablauf

1. **Verbinden:** Firmencode im Assistenten, per `WhisperLoom join <serverUrl> <code>` oder per Provisioning-Datei
   (`management.serverUrl` + `management.code`) → `POST /api/v1/enroll`. Die App verwirft den Code danach.
2. **Warten:** `state = pending` → `GET /api/v1/device` alle `pollAfterSec` Sekunden (nur solange das Fenster offen ist,
   sonst beim Start und im Sync-Intervall).
3. **Abgleich:** ab `approved` → `GET /api/v1/config` mit `If-None-Match`, beim Start und alle `syncIntervalSec`.
   Die Antwort läuft durch dieselbe Logik wie eine Provisioning-Datei (`Provisioning.Plan`): Werte, Richtlinie (drei
   Stufen) und – nur bei `distribute` – Keys. Danach `management.lastSync`.
4. **Token erneuern:** wenn `GET /device` bzw. `GET /config` `rotateToken: true` meldet → `POST /api/v1/device/token`.
5. **Abmelden:** `POST /api/v1/device/leave` (nur wenn `management.allowLeave`).

## 3. Routen

### 3.1 `POST /api/v1/enroll`

Tauscht den Firmencode gegen ein Geräte-Token. Ohne Anmeldung.

Anfrage:
```json
{"code": "ABCD-EFGH-JKMN-PQRS", "deviceName": "PC-Buchhaltung-3", "platform": "windows", "appVersion": "1.0.0"}
```
- `code` Pflicht. `deviceName` optional (≤ 64 Zeichen, Vorschlag für das Portal; Desktop: Rechnername, Android:
  `Build.MODEL`). `platform` Pflicht (≤ 20 Zeichen). `appVersion` optional (≤ 20 Zeichen).

Antwort `201`:
```json
{
  "deviceId": "d_7k3m9q2x4w8z",
  "deviceToken": "wld_test-token-not-real-0000000000000000000000",
  "state": "pending",
  "tenantName": "Muster GmbH",
  "pollAfterSec": 10
}
```
- Das Token kommt **nur einmal**. `state` ist `approved`, wenn der Code „automatisch freigeben“ hat.

Fehler: `400 invalid_request`, `404 code_invalid`, `403 tenant_blocked`, `409 too_many_pending`, `426 client_outdated`,
`429 rate_limited`.

### 3.2 `GET /api/v1/device`

Zustand des eigenen Geräts. Bearer. Auch für gesperrte Geräte `200` (kein `403`), damit die App den Zustand zeigt.

Antwort `200`:
```json
{
  "deviceId": "d_7k3m9q2x4w8z",
  "deviceName": "PC-Buchhaltung-3",
  "state": "approved",
  "tenantName": "Muster GmbH",
  "configVersion": "c_42",
  "rotateToken": false,
  "pollAfterSec": 10
}
```

Fehler: `401 device_unknown`, `429 rate_limited`.

### 3.3 `GET /api/v1/config`

Konfiguration des Geräts: Werte, Richtlinie, Key-Modus. Bearer, nur `approved`.

Anfrage-Header: `If-None-Match: "c_42"` (optional, ETag der letzten Antwort).

Antwort `304 Not Modified` ohne Body, wenn sich nichts geändert hat. Sonst `200` mit `ETag: "c_43"`:
```json
{
  "configVersion": "c_43",
  "tenantName": "Muster GmbH",
  "keyMode": "proxy",
  "proxy": {"baseUrl": "https://wl.example.com/api/v1/proxy"},
  "syncIntervalSec": 900,
  "rotateToken": false,
  "settings": {
    "schemaVersion": 1,
    "stt": {"providerId": "custom", "baseUrl": "https://wl.example.com/api/v1/proxy", "model": "whisper-large-v3-turbo"},
    "llm": {"providerId": "same", "model": "llama-3.3-70b"},
    "refine": {"stage": "polish"},
    "output": {"alwaysCopy": true},
    "selection": {"floatingButton": false},
    "management": {"allowLeave": false}
  },
  "policy": {
    "levels": {
      "stt": "fixed",
      "llm": "fixed",
      "refine.stage": "default",
      "output.alwaysCopy": "default",
      "selection.floatingButton": "fixed",
      "management.allowLeave": "fixed"
    },
    "historyEnabled": true,
    "updatesEnabled": false,
    "allowedProviders": ["custom"]
  }
}
```

- `settings` ist eine Teil-Datei im [Einstellungsformat v1](settings-format.md); sie enthält genau die Werte der Pfade
  mit Stufe `fixed` oder `default`. `policy` hat dieselben Felder wie der Abschnitt `policy` einer Provisioning-Datei
  (`levels`, `historyEnabled`, `updatesEnabled`, `allowedProviders`; `lockSettings` darf fehlen). Die App wendet beides
  an wie ein Provisioning; Vorgaben fahren nicht über Änderungen des Nutzers (`management.overrides`).
- Ein Pfad ohne Eintrag in `levels` ist für das Gerät **Frei**: Was ein früherer Abgleich dort fest gesetzt hatte, wird
  wieder freigegeben (der Wert bleibt). Tastenkürzel sind nie `fixed` (der Client behandelt sie als `default`).
  `management.*` darf der Server nur als `management.allowLeave` setzen.
- **`keyMode: "proxy"`**: `secrets` fehlt. Erkennung und Textmodell zeigen auf `proxy.baseUrl`; die App
  authentifiziert Anfragen an diese Adresse mit dem Geräte-Token (§1.1), nicht mit einem Key. Gespeicherte Keys aus
  einem früheren `distribute` löscht die App.
- **`keyMode: "distribute"`**: Feld `secrets` wie in einer Provisioning-Datei (Zugangsname oder Anbieter-ID → Key).
  Die App legt die Keys im Secret-Store ab; verteilte Keys sind gesperrt (`secrets.<zugang>` gilt als Fest). `proxy`
  fehlt.
  ```json
  {"configVersion": "c_44", "keyMode": "distribute", "secrets": {"groq": "test-key-not-real-1"}, "settings": {"stt": {"providerId": "groq"}}, "policy": {"levels": {"stt": "fixed"}}}
  ```

Fehler: `401 device_unknown`, `403 device_pending`, `403 device_blocked`, `403 tenant_blocked`, `429 rate_limited`.

### 3.4 `POST /api/v1/device/token`

Token-Rotation. Bearer (aktuelles Token). Body leer oder `{}`.

Antwort `200`:
```json
{"deviceToken": "wld_test-token-not-real-1111111111111111111111"}
```
- Das alte Token bleibt gültig, bis das neue zum ersten Mal benutzt wird, höchstens 24 h. So sperrt eine verlorene
  Antwort das Gerät nicht aus. Die App speichert das neue Token, bevor sie es benutzt.

Fehler: `401 device_unknown`, `403 device_blocked`, `429 rate_limited`.

### 3.5 `POST /api/v1/device/leave`

Gerät meldet sich selbst ab. Bearer. Body `{}`.

Antwort `204` ohne Body. Danach ist das Token ungültig. Die App löscht Token und verteilte Keys, setzt
`management.*` zurück (`state = "none"`), entfernt die Richtlinie des Servers und behält die übrigen Werte als Frei.

Fehler: `401 device_unknown` (gilt der App als „schon abgemeldet“), `403 leave_not_allowed`.

### 3.6 `GET /api/v1/proxy/models`

Nur `keyMode: "proxy"`. OpenAI-kompatible Modellliste der für das Gerät freigegebenen Modelle. Bearer.

Antwort `200`:
```json
{"object": "list", "data": [{"id": "whisper-large-v3-turbo", "object": "model", "owned_by": "groq"}, {"id": "llama-3.3-70b", "object": "model", "owned_by": "groq"}]}
```
- Ergänzt (W2-SRV2): Die Liste enthält die Modelle, die die Firma an ihren Zugängen festgelegt hat (`owned_by` = Anbieter
  des Zugangs); ohne festgelegtes Modell ist `data` leer. Fehler wie §3.7.

### 3.7 `POST /api/v1/proxy/audio/transcriptions` und `POST /api/v1/proxy/chat/completions`

Nur `keyMode: "proxy"`. OpenAI-kompatibel (Felder wie OpenAI), der Server ersetzt das Geräte-Token durch den Key des
Anbieters und übersetzt bei Bedarf (W2-SRV2). Audio: `multipart/form-data`, höchstens 25 MiB.

`/audio/transcriptions`: Felder `file` (WAV), `model`, optional `language`, `prompt`. Antwort `200`:
```json
{"text": "Hallo Welt"}
```

`/chat/completions` Anfrage:
```json
{"model": "llama-3.3-70b", "messages": [{"role": "system", "content": "…"}, {"role": "user", "content": "…"}], "temperature": 0.2}
```
Antwort `200`:
```json
{"id": "chatcmpl-1", "object": "chat.completion", "choices": [{"index": 0, "message": {"role": "assistant", "content": "…"}, "finish_reason": "stop"}]}
```

Fehler: `401 device_unknown`, `403 device_blocked`, `403 budget_exhausted`, `413 payload_too_large`,
`429 rate_limited`, `502 upstream_error`, `504 upstream_timeout`.

**Ergänzungen (W2-SRV2, additiv):**
- **Antwortfelder:** `/audio/transcriptions` liefert optional `language` (erkannte Sprache, wie vom Anbieter gemeldet) und
  optional `usage`. `/chat/completions` liefert zusätzlich `model` und `usage`
  (`{"prompt_tokens", "completion_tokens", "total_tokens"}`); `finish_reason` ist `stop` oder `length`.
- **Anfragefelder:** `/audio/transcriptions` nimmt `file`, `model`, `language`, `languages[]`, `prompt`,
  `response_format`, `temperature` (jedes höchstens einmal); `/chat/completions` nimmt `model`, `messages` (Rollen
  `system`, `user`, `assistant`, `content` als Text), `temperature`, `top_p`, `max_tokens`, `max_completion_tokens`,
  `reasoning_effort`, `thinking`, `stream` (nur `false`). Anderes → `400 invalid_request`. Die Antwort ist immer JSON.
- **Grenzen:** Audio-Body höchstens 25 MiB (+ 64 KiB Multipart-Rahmen), Text-Body höchstens 1 MiB. Große Bodies liest
  der Server erst nach Prüfung des Geräte-Tokens (sonst `401`/`403` ohne Lesen); gleichzeitige Uploads je Gerät und
  Firma sind begrenzt (`429` mit `Retry-After`), je Instanz `503`. Ein Body ohne Daten über 15 s bzw. länger als
  300 s → `400 invalid_request`.
- **Zugänge und Modell:** Erkennung nutzt die Zugänge `stt` und `stt_fallback` der eigenen Firma, Text `llm` und
  `llm_fallback` (ohne `llm` den Erkennungs-Zugang). Legt ein Zugang ein Modell fest, geht dieses an den Anbieter, sonst
  das Modell der Anfrage; `GET /config` setzt dann `stt.model` bzw. `llm.model` als `fixed`.
- **Ersatz:** Den Ersatz-Zugang wählt der Server bei Netzfehler, Zeitlimit, `401`/`403`/`429`/`5xx` oder unbrauchbarer
  Antwort des Anbieters; nicht bei `400`/`404`/`413`/`422` (Fehler der Anfrage). Zeitlimits wie die App: Verbindung
  15 s, mit Ersatz 8 s; Antwort 90 s, eigene Server (`custom`, `ollama`) 600 s.
- **Ziel-Prüfung (W2-SFIX, additiv):** Ein Zugang, dessen Basis-URL gegen die SSRF-Regeln verstößt (Fragment `#`, Query
  `?`, Benutzerinfo `@`, Backslash, Leer- oder Steuerzeichen, Schema außer `http`/`https`, internes Ziel bzw. Port
  außerhalb von `WL_UPSTREAM_ALLOW`), gilt je Anfrage als Fehlschlag mit Ersatz: Der Server verbindet sich nicht mit
  diesem Ziel, der Ersatz-Zugang springt ein, sonst `502 upstream_error`. Die Ursache steht nur im Server-Log (`ssrf_…`).
- **Budget:** Monatsdeckel (UTC-Monat) je Firma, Gruppe und Gerät für Anfragen, Audio-Minuten, Tokens und geschätzte
  Kosten. Ist eine Grenze erreicht, antwortet der Server `403 budget_exhausted`, ohne den Anbieter zu fragen; einen
  Ersatz-Anbieter gibt es dann nicht. Laufende Anfragen zählen bei der Prüfung mit (Anzahl, Audio-Minuten).
- **Modell ohne Preis (W2-FIXS, additiv):** Gilt für das Gerät ein Kostendeckel, nutzt der Server nur Zugänge, deren
  Modell (festes Zugangsmodell, sonst `model` der Anfrage) in der Preistabelle der Firma steht. Ein Zugang ohne Preis
  wird übersprungen, ohne den Anbieter zu fragen; der Ersatz-Zugang springt ein, sonst `403 budget_exhausted`.
- **Vokabular und Sprachen (W3-REST, additiv):** `/audio/transcriptions` nimmt zusätzlich `keywords[]` (Vokabular als
  Einzelbegriffe, höchstens 100 verschiedene, je 1–49 Zeichen, höchstens 5 Wörter, ohne `<`, `>`, `,`, Zeilenumbruch,
  ohne Leerraum am Rand) und `languages[]` bis zu zweimal (verschiedene Codes `[a-z]{2,3}`, z. B. `de` und `en` für
  „Deutsch + Englisch“); `language` und `languages[]` nicht zugleich. Abweichend von „jedes höchstens einmal“ dürfen nur
  diese beiden Felder mehrfach vorkommen; alles andere → `400 invalid_request`. Der Server übersetzt je Anbieter des
  Zugangs wie die App bei Direktzugang: OpenAI-Modelle `gpt-…transcribe` → `languages[]` und `keywords[]`; Mistral →
  `context_bias[]` (Leerraum → `_`, höchstens 100); ElevenLabs → `keyterms` (ohne `{}[]\`), `language_code` nur bei
  genau einer Sprache; alle anderen → `language` nur bei genau einer Sprache, kein Vokabular-Feld (`prompt` wie bisher).
- **Weitere Fehler:** `400 invalid_request`, `403 device_pending`, `403 tenant_blocked`, `404 not_found` (§1.2).
- **Datenschutz:** Audio und Text bleiben im Arbeitsspeicher der Anfrage. Der Server speichert je Gerät, Gruppe und Monat
  nur Zähler (Anfragen, Audio-Dauer, Tokens, geschätzte Kosten, Fehler je Klasse) und reicht Antworten des Anbieters nur
  normalisiert weiter (nie Fehlertexte oder Header des Anbieters).

## 4. Versionierung

- Neue Felder und neue Fehlercodes sind additiv (Client ignoriert Unbekanntes, behandelt unbekannte `error` nach HTTP-Status).
- Neue Routen bleiben unter `/api/v1`. Ein Bedeutungswechsel oder ein entferntes Feld braucht `/api/v2`; der Server
  bietet `/api/v1` dann weiter an, bis `minAppVersion` es ausschließt.
