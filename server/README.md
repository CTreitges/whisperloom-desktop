# WhisperLoom Firmen-Manager (Server)

[Deutsch](#deutsch) · [English](#english)

Selbst hostbarer, mandantenfähiger Dienst, mit dem eine Firma WhisperLoom zentral verwaltet: Geräte-Freigabe per
Firmencode, Gruppen, Richtlinie mit drei Stufen je Einstellung (Fest / Vorgabe / Frei), Keys der Anbieter, Admins.
Client-API: [`docs/server-api.md`](../docs/server-api.md). Der Proxy für Erkennung und Text (Key-Modus „Proxy“) folgt
mit W2-SRV2.

---

## Deutsch

### Überblick

| Teil | Was |
|---|---|
| Portal | `/` – Übersicht, Geräte, Gruppen, Richtlinie, Keys, Admins, Firma; Instanz-Seite `/instanz` (Firmen) |
| Client-API | `/api/v1/enroll`, `/api/v1/device`, `/api/v1/config`, `/api/v1/device/token`, `/api/v1/device/leave` |
| Gesundheit | `/healthz` → `{"status": "ok"}` |
| Daten | SQLite in `WL_DATA_DIR` (`whisperloom-server.db`), Hauptschlüssel `master.key` |

Mehrere Firmen je Instanz. Jede Firma hat eigenen Firmencode (rotierbar), eigene Admins (Rollen owner/admin), Gruppen,
Geräte, Keys und Richtlinie. Der Instanz-Admin legt Firmen an und sperrt sie, sieht aber keine Geräte, Keys oder
Richtlinien. Für eine einzelne Firma reicht eine Firma.

### Installation mit Docker

```sh
cd server
docker compose up -d                 # baut das Image, lauscht auf 127.0.0.1:8080
docker compose exec whisperloom-server python -m whisperloom_server tenant-create muster "Muster GmbH" --owner chef --show
```

Die Ausgabe nennt den Firmencode und einen Einladungs-Link (einmal gültig, 72 h) für den ersten owner. Ohne `--show`
landen beide in einer Datei mit Rechten 600 unter `/data/ausgabe/` statt auf dem Bildschirm.

Das Image läuft als Benutzer `whisperloom` (UID 10001), Daten liegen im Volume `/data`, der Healthcheck fragt
`/healthz`. Das Compose-Beispiel setzt zusätzlich `read_only`, `cap_drop: ALL` und `no-new-privileges`.

### Installation ohne Docker (systemd)

```sh
cd server
py -3 -m venv .venv                         # Linux: python3 -m venv .venv
.venv/Scripts/python.exe -m pip install -r requirements.txt -e .   # Linux: .venv/bin/python -m pip …
export WL_DATA_DIR=/var/lib/whisperloom WL_BASE_URL=https://wl.example.com
.venv/bin/python -m whisperloom_server init
.venv/bin/python -m whisperloom_server tenant-create muster "Muster GmbH" --owner chef
.venv/bin/python -m whisperloom_server serve --host 127.0.0.1 --port 8080
```

systemd-Unit (Beispiel `/etc/systemd/system/whisperloom-server.service`):

```ini
[Unit]
Description=WhisperLoom Firmen-Manager
After=network-online.target

[Service]
User=whisperloom
WorkingDirectory=/opt/whisperloom/server
Environment=WL_DATA_DIR=/var/lib/whisperloom
Environment=WL_BASE_URL=https://wl.example.com
LoadCredential=master.key:/etc/whisperloom/master.key
ExecStart=/opt/whisperloom/server/.venv/bin/python -m whisperloom_server serve --host 127.0.0.1 --port 8080
UMask=0077
NoNewPrivileges=true
PrivateTmp=true
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Mit `LoadCredential` liest der Server den Hauptschlüssel aus `$CREDENTIALS_DIRECTORY/master.key`. Datei-Modus neu laden:
`systemctl kill -s HUP whisperloom-server`.

Für Tests (aus `server/`): `.venv/Scripts/python.exe -m pip install -r requirements-dev.txt -e .`, dann
`.venv/Scripts/python.exe -m pytest server -q` (aus dem Repo) und `py -3 -X utf8 server/tools/smoke_server.py`.

### Konfiguration (Umgebungsvariablen)

| Variable | Standard | Bedeutung |
|---|---|---|
| `WL_DATA_DIR` | `~/.local/share/whisperloom-server` (Docker: `/data`) | Datenordner (DB, `master.key`, `ausgabe/`) |
| `WL_BASE_URL` | `http://127.0.0.1:8080` | Öffentliche Adresse; `https://` → Cookies `Secure` mit `__Host-`-Präfix |
| `WL_AUTH_MODES` | `password_totp` | Anmeldearten, kommagetrennt (siehe unten) oder genau `file` |
| `WL_TRUSTED_PROXIES` | – | Genaue Adresse(n) des Reverse-Proxys, z. B. `127.0.0.1/32` oder `172.30.0.10/32`; nie ganze Docker- oder Firmennetze (siehe trusted_header) |
| `WL_TRUSTED_HEADER` | `Remote-User` | Header mit dem Benutzernamen (trusted_header) |
| `WL_CONFIG_FILE` | – | YAML- oder JSON-Datei im Datei-Modus |
| `WL_MASTER_KEY` / `WL_MASTER_KEY_FILE` | – | Hauptschlüssel (64 Hex-Zeichen) direkt bzw. als Datei |
| `WL_MIN_APP_VERSION` | – | Ältere Apps erhalten beim Verbinden `426 client_outdated` |
| `WL_UPSTREAM_ALLOW` | – | Interne Netze, die Anbieter-Adressen des Proxys erreichen dürfen, z. B. `10.20.0.0/16` oder `127.0.0.1/32` (siehe „SSRF-Schutz“) |

Nur von `WL_TRUSTED_PROXIES` wertet der Server `X-Forwarded-For` aus (Client-IP für Rate-Limits); uvicorn läuft ohne
`--proxy-headers`.

### Reverse-Proxy

Caddy:

```caddy
wl.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

nginx:

```nginx
server {
    listen 443 ssl http2;
    server_name wl.example.com;
    # ssl_certificate …; ssl_certificate_key …;
    client_max_body_size 64k;
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}
```

Dazu `WL_TRUSTED_PROXIES=127.0.0.1/32`, damit Rate-Limits die echte Client-IP sehen.

### Anmeldearten

`WL_AUTH_MODES` legt fest, was die Instanz erlaubt. Jede Firma kann auf der Seite „Firma“ weiter einschränken (owner).

| Art | Verhalten | Beispiel |
|---|---|---|
| `password_totp` (Standard) | Passwort + 6-stelliger Code einer Authenticator-App. Wer noch kein TOTP hat, richtet es beim ersten Login ein. | `WL_AUTH_MODES=password_totp` |
| `password` | Passwort, TOTP freiwillig je Konto („2FA abschaltbar“). Eine Firma, die nur `password_totp` erlaubt, verlangt TOTP trotzdem. | `WL_AUTH_MODES=password_totp,password` |
| `trusted_header` | Vorgeschaltete Anmeldung (Authelia, oauth2-proxy, Authentik). Der Header mit dem Benutzernamen gilt nur von `WL_TRUSTED_PROXIES`; von jeder anderen Adresse wird er ignoriert. Der Benutzername muss als Admin angelegt sein. | `WL_AUTH_MODES=trusted_header WL_TRUSTED_PROXIES=127.0.0.1/32 WL_TRUSTED_HEADER=Remote-User` |
| `local_only` | Portal ohne Login, nur direkt auf dem Server-Rechner (`127.0.0.1`/`::1`, lokaler Host-Header, kein `X-Forwarded-*`). Von anderen Adressen: 403. Nicht hinter einem Reverse-Proxy auf demselben Rechner verwenden. Im Docker-Container wirkungslos (Gegenstelle ist das Docker-Netz). | `WL_AUTH_MODES=local_only WL_BASE_URL=http://127.0.0.1:8080` |
| `file` | Kein Portal, kein Admin-Login. Firmen, Gruppen, Richtlinie und Keys aus `WL_CONFIG_FILE`. Geräte freigeben per `autoApprove` oder CLI `device-approve`. | `WL_AUTH_MODES=file WL_CONFIG_FILE=/etc/whisperloom/firmen.yaml` |

**Wichtig bei `trusted_header`:** Jeder Rechner bzw. Prozess, der von einer Adresse in `WL_TRUSTED_PROXIES` aus den
Server erreicht, kann sich als beliebiger Admin ausgeben, auch als Instanz-Admin. Deshalb:

- `WL_TRUSTED_PROXIES` nur auf die genaue Adresse des Proxys setzen (`/32`), nie auf `172.16.0.0/12` oder ein ganzes LAN.
  Im Compose-Netz dem Proxy eine feste IP geben.
- Läuft der Proxy auf demselben Rechner (`127.0.0.1/32`), vertraut der Server jedem lokalen Prozess. Den Server dann an
  eine eigene Adresse/einen Unix-Socket des Proxys binden oder in ein eigenes Netz legen.
- Der Proxy muss den Header bei jeder Anfrage selbst setzen bzw. überschreiben (Caddy `copy_headers`, nginx
  `proxy_set_header Remote-User $upstream_http_remote_user;`). Kommt der Header mehrfach an, lehnt der Server ab.

Beispiel Authelia/Caddy:

```caddy
wl.example.com {
    forward_auth authelia:9091 {
        uri /api/authz/forward-auth
        copy_headers Remote-User
    }
    reverse_proxy whisperloom-server:8080
}
```

Datei-Modus: Beispiel [`beispiele/firmen.yaml`](beispiele/firmen.yaml). Werte `env:NAME` liest der Server aus der
Umgebung (Keys, Codes). Ein Fehler in der Datei ändert nichts (vorheriger Stand bleibt, Log nennt die Fehlerklasse);
beim Start bricht der Server ab. Firmen, die aus der Datei verschwinden, werden gesperrt, nicht gelöscht.

### Geräte sperren bei automatischer Freigabe

Ist „Neue Geräte automatisch freigeben“ an, kann sich ein gesperrtes Gerät mit dem bekannten Firmencode sofort neu
verbinden und ist wieder freigegeben (im Key-Modus „Verteilen“ samt Keys). Nach dem Sperren deshalb den Firmencode
erneuern (Seite „Firma“ bzw. `code-rotate`); das Portal weist beim Sperren darauf hin. Verbundene Geräte bleiben
verbunden.

### Richtlinie (drei Stufen)

Je Einstellung: **Fest** (zentral, in der App ausgeblendet), **Vorgabe** (vorbelegt, änderbar), **Frei** (nichts
zentral). Gruppen überschreiben die Firma je Einstellung, auch mit „Frei“; „Wie Firma“ erbt. Tastenkürzel sind nie Fest.
Im Key-Modus „Proxy“ setzt der Server Erkennung und Textmodell fest auf den Proxy.

### Keys und Hauptschlüssel

- Keys liegen AES-256-GCM-verschlüsselt in der DB (Schlüssel per HKDF aus dem Hauptschlüssel); angezeigt werden nur die
  letzten vier Zeichen. Geräte-Token, Firmencodes (Suche) und Sitzungen nur als Hash.
- **Key-Modus je Firma:** `proxy` (Standard) – Keys verlassen den Server nie; `distribute` – freigegebene Geräte erhalten
  die Keys bei `GET /api/v1/config`, nie wartende oder gesperrte.
- Hauptschlüssel: `init` legt `master.key` (Rechte 600) an, wenn weder `WL_MASTER_KEY` noch `WL_MASTER_KEY_FILE` gesetzt
  ist. Ohne den Hauptschlüssel sind Keys und Firmencodes im Backup nicht lesbar; geht er verloren, müssen Keys neu
  eingetragen und Codes rotiert werden.
- **`master.key` nicht im selben Backup bzw. Volume wie die DB ablegen.** Liegt beides zusammen (Standard: beides in
  `WL_DATA_DIR`), entschlüsselt jeder mit dem Backup auch Keys und Firmencodes. Für den Betrieb den Schlüssel per
  `WL_MASTER_KEY_FILE` (Docker-Secret unter `/run/secrets/…`), systemd `LoadCredential=master.key:…` oder
  `WL_MASTER_KEY` aus einem Secret-Store liefern und `master.key` aus dem Datenordner entfernen.
- Firmencodes liegen als HMAC (Suche) und zusätzlich AES-GCM-verschlüsselt (damit das Portal sie jederzeit anzeigen
  kann); Geräte-Token, Sitzungen und Einladungen nur als Hash.

### Proxy (Key-Modus „Proxy“)

Geräte schicken Erkennung und Textverbesserung an `<WL_BASE_URL>/api/v1/proxy` (OpenAI-kompatibel, Anmeldung mit dem
Geräte-Token); der Server ersetzt das Token durch den Key der Firma und spricht den Anbieter in dessen Protokoll an:
OpenAI-kompatibel (OpenAI, Groq, Mistral, Together, DeepInfra, OpenRouter, DeepSeek, eigener Server), ElevenLabs,
Anthropic Messages, Gemini und Ollama. Die App braucht dafür keine Sonderlogik (Anbieter „Eigener Server“).

- **Zugänge** (Seite „Keys“ oder `key-set`): `stt` und `stt_fallback` für die Erkennung, `llm` und `llm_fallback` für
  Text (ohne `llm` nimmt der Server den Erkennungs-Zugang). Optional je Zugang ein **Modell**: Dann geht genau dieses
  Modell an den Anbieter, egal was das Gerät schickt. Eigene Server (`custom`, `ollama`) dürfen ohne Key sein.
- **Ersatz:** Bei Netzfehler, Zeitlimit, `401`/`403`/`429`/`5xx` oder unbrauchbarer Antwort nimmt der Server den
  `…_fallback`-Zugang; nicht bei Fehlern der Anfrage (`400`/`404`/`413`/`422`). Zeitlimits wie die App: Verbindung 15 s
  (mit Ersatz 8 s), Antwort 90 s, eigene Server 600 s.
- **Grenzen:** Audio als WAV bis 25 MiB, Text bis 1 MiB. Gerät muss freigegeben sein, die Firma nicht gesperrt.
  Höchstens 20 gleichzeitige Anbieter-Anfragen je Firma (sonst `429` mit `Retry-After`) und 16 gleichzeitige große
  Uploads je Instanz (sonst `503`); jede Anbieter-Anfrage endet spätestens nach Antwort- plus Verbindungs-Zeitlimit.
- **Kosten verlässlich schätzen:** Modell am Zugang festlegen. Sonst rechnet der Server mit dem Modellnamen der Anfrage,
  und ein Name ohne Eintrag in der Preistabelle zählt 0. Fehlgeschlagene Anfragen zählen nur als Fehler je Klasse.
- Zugang per Kommandozeile (Key über stdin, nie als Argument):
  `echo "$KEY" | python -m whisperloom_server key-set acme stt --provider groq --model whisper-large-v3-turbo`

### SSRF-Schutz

Anbieter-Adressen trägt der Firmen-Admin ein. Damit niemand über den Proxy interne Dienste der Instanz erreicht,
lehnt der Server Ziele in Loopback (`127.0.0.0/8`, `::1`), privaten Netzen (`10/8`, `172.16/12`, `192.168/16`, ULA),
link-local (inkl. Metadaten `169.254.169.254`), CGNAT, Multicast und reservierten Bereichen ab, ebenso andere Schemata
als `http`/`https` (z. B. Unix-Sockets) und Zugangsdaten in der URL. Geprüft wird beim Speichern und bei **jeder**
Anfrage: Der Server löst den Namen selbst auf, prüft alle Adressen und verbindet sich mit genau der geprüften
(DNS-Rebinding); Weiterleitungen folgt er nicht.

Self-Hosting im Firmennetz (eigener whisper-server, Ollama): das Netz bzw. die Adresse per `WL_UPSTREAM_ALLOW`
freigeben, so eng wie möglich, z. B. `WL_UPSTREAM_ALLOW=10.20.3.4/32`. In einer Mehrmandanten-Instanz gilt die Freigabe
für alle Firmen – dort nur Netze freigeben, die jede Firma erreichen darf.

### Nutzung und Budget

- **Nutzung** (Seite „Nutzung“, CSV-Export): je Monat (UTC) und Gerät, Gruppe und Firma die Zahl der beantworteten
  Anfragen, Audio-Minuten, Tokens (laut Anbieter-Antwort), geschätzte Kosten und Fehler je Klasse (Anbieter-Fehler,
  Zeitüberschreitung, Budget erreicht, ungültige Anfrage).
- **Budget** (Seite „Budget“, nur owner ändern): Monatsdeckel je Firma, Gruppe oder Gerät für Anfragen, Audio-Minuten,
  Tokens und/oder Kosten, mit Warnschwelle (Standard 80 %). Ist eine Grenze erreicht, lehnt der Proxy bis Monatsende mit
  `403 budget_exhausted` ab, ohne den Anbieter zu fragen; die App weicht auf ihren Ersatz aus. Die Grenze ist weich:
  gleichzeitige Anfragen können sie knapp überschreiten.
- **Preistabelle** (Seite „Budget“): Preis je Modell und Audio-Minute bzw. je 1 Mio. Tokens ein/aus, Grundlage der
  Kosten-Schätzung. Ohne Eintrag zählt ein Modell 0.

### Datenschutz: was der Server speichert und was nicht

| Gespeichert | Nicht gespeichert |
|---|---|
| Firmen, Gruppen, Admins (Passwort als Argon2-Hash, TOTP verschlüsselt), Richtlinie | Diktat-Audio (bleibt im Arbeitsspeicher der Anfrage, auch kein Temp-File) |
| Anbieter-Keys AES-256-GCM-verschlüsselt, angezeigt nur die letzten 4 Zeichen | Erkannter Text, Text vor und nach der Verbesserung, Prompts |
| Geräte (Name, Plattform, App-Version, Zustand), Geräte-Token nur als SHA-256-Hash | Header und Fehlertexte der Anbieter |
| Nutzungszähler je Gerät/Gruppe/Monat, Budgets, Preistabelle | Geräte-Token und Keys im Log |
| Audit-Log der Admin-Aktionen, Log-Zeilen mit Metadaten (Gerät, Zugang, Anbieter, Status, Dauer, Fehlerklasse) | IP-Adressen im Klartext (nur als HMAC für Login-Sperren) |

Der Anbieter selbst erhält Audio bzw. Text – seine Datenschutzbedingungen gelten zusätzlich.

### Backup


```sh
sqlite3 "$WL_DATA_DIR/whisperloom-server.db" ".backup '/backup/wl-$(date +%F).db'"
# Docker:
docker compose exec whisperloom-server python -c "import sqlite3; s=sqlite3.connect('/data/whisperloom-server.db'); d=sqlite3.connect('/data/backup.db'); s.backup(d)"
```

Den Hauptschlüssel einmalig getrennt sichern, nie im selben Backup wie die DB. Wiederherstellen: Dienst stoppen,
DB-Datei ersetzen, starten.

### Kommandozeile

`python -m whisperloom_server --help`: `serve`, `init`, `instance-admin-create`, `tenant-create`, `tenant-list`,
`code-rotate`, `code-show`, `admin-create`, `admin-invite` (Passwort vergessen), `admin-totp-reset`, `device-list`,
`device-approve`, `device-block`, `key-set` (Zugang speichern, Key von stdin).

### Sicherheit

CSRF-Token + Origin-Prüfung auf jedem Formular, Cookies `HttpOnly`/`SameSite=Strict` (bei https `Secure`, `__Host-`),
CSP ohne Inline-Skript, Rate-Limits für Login, TOTP und Enrollment, Konto-Sperre nach 5 Fehlversuchen (15 min),
Body-Grenze 64 KiB (Proxy: Audio 25 MiB, Text 1 MiB), SSRF-Schutz für Anbieter-Adressen, Audit-Log der Admin-Aktionen (nur Metadaten), Logs ohne Keys, Token, Codes oder Inhalte.

---

## English

### Overview

Self-hostable, multi-tenant service for managing WhisperLoom centrally: device approval via company code, groups, a
three-level policy per setting (Locked / Default / Free), provider keys, admins. Client API:
[`docs/server-api.md`](../docs/server-api.md), including the recognition/text proxy (key mode “proxy”).

Several companies per instance, each with its own rotatable company code, admins (roles owner/admin), groups, devices,
keys and policy. The instance admin creates and blocks companies but never sees their devices, keys or policies.

### Install with Docker

```sh
cd server
docker compose up -d
docker compose exec whisperloom-server python -m whisperloom_server tenant-create acme "ACME Ltd" --owner boss --show
```

The output contains the company code and a one-time invitation link (72 h) for the first owner. Without `--show` both
go to a file with mode 600 in `/data/ausgabe/`. The image runs as user `whisperloom` (UID 10001), data lives in the
`/data` volume, the health check calls `/healthz`.

### Install without Docker (systemd)

```sh
cd server
python3 -m venv .venv && .venv/bin/python -m pip install -r requirements.txt -e .
export WL_DATA_DIR=/var/lib/whisperloom WL_BASE_URL=https://wl.example.com
.venv/bin/python -m whisperloom_server init
.venv/bin/python -m whisperloom_server tenant-create acme "ACME Ltd" --owner boss
.venv/bin/python -m whisperloom_server serve --host 127.0.0.1 --port 8080
```

Use the systemd unit from the German section (`LoadCredential=master.key:…` keeps the master key outside the data
directory). Reload file mode with `systemctl kill -s HUP whisperloom-server`.

### Configuration

Environment variables as in the German table: `WL_DATA_DIR`, `WL_BASE_URL`, `WL_AUTH_MODES`, `WL_TRUSTED_PROXIES`,
`WL_TRUSTED_HEADER`, `WL_CONFIG_FILE`, `WL_MASTER_KEY` / `WL_MASTER_KEY_FILE`, `WL_MIN_APP_VERSION`,
`WL_UPSTREAM_ALLOW` (internal networks the proxy may reach, see “SSRF protection”). The server reads `X-Forwarded-For`
only from `WL_TRUSTED_PROXIES`.

### Reverse proxy

See the Caddy and nginx examples above; set `WL_TRUSTED_PROXIES=127.0.0.1/32` so rate limits see the real client IP.

### Sign-in methods

| Method | Behaviour |
|---|---|
| `password_totp` (default) | Password plus 6-digit authenticator code; accounts without TOTP set it up at first sign-in. |
| `password` | Password, TOTP optional per account (two-factor can be turned off). A company allowing only `password_totp` still requires it. |
| `trusted_header` | Upstream sign-in (Authelia, oauth2-proxy, Authentik). The user-name header counts only from `WL_TRUSTED_PROXIES` and is ignored from any other address. The user name must exist as an admin. Trust only the proxy's exact address (`/32`), never a whole Docker or office network: anything sending from a trusted address can claim any admin. The proxy must set or overwrite the header on every request; a repeated header is rejected. |
| `local_only` | Portal without sign-in, only directly on the server machine (`127.0.0.1`/`::1`, local Host header, no `X-Forwarded-*`). Other addresses get 403. Do not use behind a reverse proxy on the same machine; no effect inside Docker. |
| `file` | No portal, no admin sign-in. Companies, groups, policy and keys come from `WL_CONFIG_FILE` (YAML or JSON, `env:NAME` references for secrets), re-read on SIGHUP and on file change. Approve devices via `autoApprove` or `device-approve`. |

Each company may restrict the instance's methods further (page “Company”, owners only).

### Policy, keys, master key

- Per setting: **Locked** (central, hidden in the app), **Default** (preset, changeable), **Free**. Groups override the
  company per setting; keyboard shortcuts are never locked.
- Keys are stored AES-256-GCM encrypted; only the last four characters are shown. Device tokens, company codes and
  sessions are stored as hashes only.
- Key mode per company: `proxy` (default, keys never leave the server) or `distribute` (approved devices receive the keys).
- Never keep `master.key` in the same backup or volume as the database (by default both live in `WL_DATA_DIR`): whoever
  has both can decrypt keys and company codes. Supply it via `WL_MASTER_KEY_FILE` (Docker secret), systemd
  `LoadCredential=master.key:…` or `WL_MASTER_KEY` from a secret store, and remove `master.key` from the data directory.
  Losing it means re-entering keys and rotating codes.
- Company codes are stored as an HMAC (lookup) and additionally AES-GCM encrypted so the portal can always show them.

### Blocking devices with automatic approval

With “Approve new devices automatically” on, a blocked device that knows the company code can connect again at once
(with keys in “distribute” mode). Renew the company code after blocking (page “Company” or `code-rotate`); the portal
points this out when you block a device.

### Proxy (key mode “proxy”)

Devices send recognition and text refinement to `<WL_BASE_URL>/api/v1/proxy` (OpenAI-compatible, authenticated with
the device token); the server swaps the token for the company's key and talks to the provider in its own protocol:
OpenAI-compatible (OpenAI, Groq, Mistral, Together, DeepInfra, OpenRouter, DeepSeek, custom server), ElevenLabs,
Anthropic Messages, Gemini and Ollama. The app needs no special logic (provider “custom server”).

- **Accesses** (page “Keys” or `key-set`): `stt` and `stt_fallback` for recognition, `llm` and `llm_fallback` for text
  (without `llm` the recognition access is used). Optionally a **model** per access: exactly this model is sent to the
  provider, whatever the device asks for. Own servers (`custom`, `ollama`) may have no key.
- **Fallback:** on network errors, timeouts, `401`/`403`/`429`/`5xx` or an unusable answer the server uses the
  `…_fallback` access; not on request errors (`400`/`404`/`413`/`422`). Timeouts as in the app: connect 15 s (8 s with a
  fallback), response 90 s, own servers 600 s.
- **Limits:** audio as WAV up to 25 MiB, text up to 1 MiB. The device must be approved and the company not blocked.
  At most 20 concurrent provider requests per company (otherwise `429` with `Retry-After`) and 16 concurrent large
  uploads per instance (otherwise `503`); every provider request ends after the response plus connect timeout.
- **Reliable cost estimates:** set the model on the access. Otherwise the server uses the model name of the request, and
  a name without a price table entry counts as 0. Failed requests only count as errors per class.
- Command line (key via stdin, never as an argument):
  `echo "$KEY" | python -m whisperloom_server key-set acme stt --provider groq --model whisper-large-v3-turbo`

### SSRF protection

Company admins enter provider addresses. So that nobody reaches internal services of the instance through the proxy,
the server rejects targets in loopback (`127.0.0.0/8`, `::1`), private networks (`10/8`, `172.16/12`, `192.168/16`,
ULA), link-local (including metadata `169.254.169.254`), CGNAT, multicast and reserved ranges, other schemes than
`http`/`https` (e.g. Unix sockets) and credentials in the URL. Checks run on save and on **every** request: the server
resolves the name itself, checks all addresses and connects to exactly the checked one (DNS rebinding); it does not
follow redirects.

Self-hosting inside the company network (own whisper server, Ollama): allow the network or address via
`WL_UPSTREAM_ALLOW`, as narrow as possible, e.g. `WL_UPSTREAM_ALLOW=10.20.3.4/32`. In a multi-tenant instance the
allowance applies to every company – only allow networks every company may reach.

### Usage and budget

- **Usage** (page “Usage”, CSV export): per month (UTC) and device, group and company the number of answered requests,
  audio minutes, tokens (as reported by the provider), estimated cost and errors per class.
- **Budget** (page “Budget”, owners change it): monthly caps per company, group or device for requests, audio minutes,
  tokens and/or cost, with a warning threshold (default 80 %). Once a limit is reached the proxy declines with
  `403 budget_exhausted` until the end of the month without asking the provider; the app falls back. The limit is soft:
  concurrent requests may exceed it slightly.
- **Price table** (page “Budget”): price per model and audio minute or per 1M tokens in/out, the basis of the cost
  estimate. Models without an entry count as 0.

### Privacy: what the server stores and what it does not

| Stored | Not stored |
|---|---|
| Companies, groups, admins (password as Argon2 hash, TOTP encrypted), policy | Dictation audio (kept in memory for the request only, no temp file) |
| Provider keys AES-256-GCM encrypted, only the last 4 characters shown | Recognised text, text before and after refinement, prompts |
| Devices (name, platform, app version, state), device tokens as SHA-256 hash only | Provider headers and error texts |
| Usage counters per device/group/month, budgets, price table | Device tokens and keys in logs |
| Admin audit log, log lines with metadata (device, access, provider, status, duration, error class) | IP addresses in plain text (only as an HMAC for sign-in locks) |

The provider itself receives the audio or text – its privacy terms apply in addition.

### Backup

Use SQLite's online backup (`sqlite3 … ".backup …"`, see above) and back up the master key once, separately.

### Security

CSRF token plus origin check on every form, `HttpOnly`/`SameSite=Strict` cookies (`Secure` and `__Host-` on https), CSP
without inline script, rate limits for sign-in, TOTP and enrollment, account lock after 5 failures (15 min), 64 KiB body
limit (proxy: audio 25 MiB, text 1 MiB), SSRF protection for provider addresses, admin audit log (metadata only), no
keys, tokens, codes or content in logs. Command line additionally: `key-set` (store an access, key via stdin).
