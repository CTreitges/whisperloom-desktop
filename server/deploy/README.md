# Demo-Instanz (systemd-User-Dienst hinter Caddy)

Vorlage für die Demo `https://whisperloom-admin.ctreitges.de` (W2-DEMO); taugt als Muster für jede Installation ohne
Docker. Allgemeine Installation: [`../README.md`](../README.md).

| Datei | Zweck |
|---|---|
| `setup-demo.sh` | venv, Hauptschlüssel und env (600), Datenordner (700), Instanz-Admin `admin`, Firma „Demo GmbH“ mit owner `demo`, Units aktivieren. Wiederholbar. |
| `env.example` | Umgebung (`WL_*`), wird nach `~/.config/whisperloom-admin/env` kopiert |
| `whisperloom-admin.service` | Server auf `127.0.0.1:8099` |
| `whisperloom-admin-fake.service` | Fake-Anbieter `tools/fake_api.py` auf `127.0.0.2:8100` (nur Loopback) |
| `wl-admin` | Admin-CLI mit der Umgebung des Dienstes; dazu `demo-fake-url`, `demo-fake-requests`, `demo-fake-reset` |
| `Caddyfile.block` | Host-Block für die Caddy-Vorlage |

## Ablauf

```sh
git clone https://github.com/CTreitges/whisperloom-desktop ~/whisperloom-admin/app
sh ~/whisperloom-admin/app/server/deploy/setup-demo.sh
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8099/healthz      # 200
# Caddy: Caddyfile.block in die Vorlage übernehmen, validieren, installieren, neu laden
```

Zugangsdaten: `~/.config/whisperloom-admin/admin-initial.txt` (600). TOTP richtet jeder Admin beim ersten Login ein.

## Sicherheit

- Anmeldeart `password_totp`, Cookies `Secure`/`__Host-` über `WL_BASE_URL=https://…`, Header setzt die App.
- `WL_TRUSTED_PROXIES=127.0.0.1/32`: nur Caddy liefert `X-Forwarded-For` (Rate-Limits je Client).
- `WL_UPSTREAM_ALLOW=127.0.0.2/32`: Der Proxy darf von den internen Adressen nur den Fake erreichen. Auf `127.0.0.1`
  lauschen andere Dienste (u. a. die Caddy-Admin-API); eine Freigabe für `127.0.0.1/32` würde sie für jeden
  Firmen-Admin erreichbar machen. Dienste, die auf allen Adressen lauschen (`0.0.0.0`), sind auch über `127.0.0.2`
  erreichbar; vor dem Freigeben `ss -tlnp` prüfen.
- Hauptschlüssel außerhalb des Datenordners (`WL_MASTER_KEY_FILE`), ein Backup von `data/` enthält ihn nicht.

## Live-Smoke

```sh
py -3 -X utf8 tools/smoke.py --platform win --suite enroll --id <ID> --work-dir <W> --app <exe> \
  --server https://whisperloom-admin.ctreitges.de --admin-cli "ssh oracle-vps whisperloom-admin/app/server/deploy/wl-admin"
```

Die Suite legt eine eigene Testfirma an und löscht sie am Ende (`tenant-delete`); „Demo GmbH“ bleibt.
