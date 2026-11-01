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
.venv/Scripts/python.exe -m pip install -r requirements.txt   # Linux: .venv/bin/python -m pip …
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

Für Tests: `.venv/Scripts/python.exe -m pip install -r requirements-dev.txt`, dann
`.venv/Scripts/python.exe -m pytest server -q` (aus dem Repo) und `py -3 -X utf8 server/tools/smoke_server.py`.

### Konfiguration (Umgebungsvariablen)

| Variable | Standard | Bedeutung |
|---|---|---|
| `WL_DATA_DIR` | `~/.local/share/whisperloom-server` (Docker: `/data`) | Datenordner (DB, `master.key`, `ausgabe/`) |
| `WL_BASE_URL` | `http://127.0.0.1:8080` | Öffentliche Adresse; `https://` → Cookies `Secure` mit `__Host-`-Präfix |
| `WL_AUTH_MODES` | `password_totp` | Anmeldearten, kommagetrennt (siehe unten) oder genau `file` |
| `WL_TRUSTED_PROXIES` | – | IPs/Netze des Reverse-Proxys, z. B. `127.0.0.1/32,172.16.0.0/12` |
| `WL_TRUSTED_HEADER` | `Remote-User` | Header mit dem Benutzernamen (trusted_header) |
| `WL_CONFIG_FILE` | – | YAML- oder JSON-Datei im Datei-Modus |
| `WL_MASTER_KEY` / `WL_MASTER_KEY_FILE` | – | Hauptschlüssel (64 Hex-Zeichen) direkt bzw. als Datei |
| `WL_MIN_APP_VERSION` | – | Ältere Apps erhalten beim Verbinden `426 client_outdated` |

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
  ist. Empfehlung: Schlüssel getrennt von der DB aufbewahren (Docker-Secret, systemd `LoadCredential`). Ohne den
  Hauptschlüssel sind Keys und Firmencodes im Backup nicht lesbar; geht er verloren, müssen Keys neu eingetragen und
  Codes rotiert werden.

### Backup

```sh
sqlite3 "$WL_DATA_DIR/whisperloom-server.db" ".backup '/backup/wl-$(date +%F).db'"
# Docker:
docker compose exec whisperloom-server python -c "import sqlite3; s=sqlite3.connect('/data/whisperloom-server.db'); d=sqlite3.connect('/data/backup.db'); s.backup(d)"
```

Den Hauptschlüssel einmalig getrennt sichern. Wiederherstellen: Dienst stoppen, DB-Datei ersetzen, starten.

### Kommandozeile

`python -m whisperloom_server --help`: `serve`, `init`, `instance-admin-create`, `tenant-create`, `tenant-list`,
`code-rotate`, `code-show`, `admin-create`, `admin-invite` (Passwort vergessen), `admin-totp-reset`, `device-list`,
`device-approve`, `device-block`.

### Sicherheit

CSRF-Token + Origin-Prüfung auf jedem Formular, Cookies `HttpOnly`/`SameSite=Strict` (bei https `Secure`, `__Host-`),
CSP ohne Inline-Skript, Rate-Limits für Login, TOTP und Enrollment, Konto-Sperre nach 5 Fehlversuchen (15 min),
Body-Grenze 64 KiB, Audit-Log der Admin-Aktionen (nur Metadaten), Logs ohne Keys, Token, Codes oder Inhalte.

---

## English

### Overview

Self-hostable, multi-tenant service for managing WhisperLoom centrally: device approval via company code, groups, a
three-level policy per setting (Locked / Default / Free), provider keys, admins. Client API:
[`docs/server-api.md`](../docs/server-api.md). The recognition/text proxy (key mode “proxy”) follows with W2-SRV2.

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
python3 -m venv .venv && .venv/bin/python -m pip install -r requirements.txt
export WL_DATA_DIR=/var/lib/whisperloom WL_BASE_URL=https://wl.example.com
.venv/bin/python -m whisperloom_server init
.venv/bin/python -m whisperloom_server tenant-create acme "ACME Ltd" --owner boss
.venv/bin/python -m whisperloom_server serve --host 127.0.0.1 --port 8080
```

Use the systemd unit from the German section (`LoadCredential=master.key:…` keeps the master key outside the data
directory). Reload file mode with `systemctl kill -s HUP whisperloom-server`.

### Configuration

Environment variables as in the German table: `WL_DATA_DIR`, `WL_BASE_URL`, `WL_AUTH_MODES`, `WL_TRUSTED_PROXIES`,
`WL_TRUSTED_HEADER`, `WL_CONFIG_FILE`, `WL_MASTER_KEY` / `WL_MASTER_KEY_FILE`, `WL_MIN_APP_VERSION`. The server reads
`X-Forwarded-For` only from `WL_TRUSTED_PROXIES`.

### Reverse proxy

See the Caddy and nginx examples above; set `WL_TRUSTED_PROXIES=127.0.0.1/32` so rate limits see the real client IP.

### Sign-in methods

| Method | Behaviour |
|---|---|
| `password_totp` (default) | Password plus 6-digit authenticator code; accounts without TOTP set it up at first sign-in. |
| `password` | Password, TOTP optional per account (two-factor can be turned off). A company allowing only `password_totp` still requires it. |
| `trusted_header` | Upstream sign-in (Authelia, oauth2-proxy, Authentik). The user-name header counts only from `WL_TRUSTED_PROXIES` and is ignored from any other address. The user name must exist as an admin. |
| `local_only` | Portal without sign-in, only directly on the server machine (`127.0.0.1`/`::1`, local Host header, no `X-Forwarded-*`). Other addresses get 403. Do not use behind a reverse proxy on the same machine; no effect inside Docker. |
| `file` | No portal, no admin sign-in. Companies, groups, policy and keys come from `WL_CONFIG_FILE` (YAML or JSON, `env:NAME` references for secrets), re-read on SIGHUP and on file change. Approve devices via `autoApprove` or `device-approve`. |

Each company may restrict the instance's methods further (page “Company”, owners only).

### Policy, keys, master key

- Per setting: **Locked** (central, hidden in the app), **Default** (preset, changeable), **Free**. Groups override the
  company per setting; keyboard shortcuts are never locked.
- Keys are stored AES-256-GCM encrypted; only the last four characters are shown. Device tokens, company codes and
  sessions are stored as hashes only.
- Key mode per company: `proxy` (default, keys never leave the server) or `distribute` (approved devices receive the keys).
- Keep the master key separate from database backups (Docker secret, systemd credential). Losing it means re-entering
  keys and rotating codes.

### Backup

Use SQLite's online backup (`sqlite3 … ".backup …"`, see above) and back up the master key once, separately.

### Security

CSRF token plus origin check on every form, `HttpOnly`/`SameSite=Strict` cookies (`Secure` and `__Host-` on https), CSP
without inline script, rate limits for sign-in, TOTP and enrollment, account lock after 5 failures (15 min), 64 KiB body
limit, admin audit log (metadata only), no keys, tokens, codes or content in logs.
