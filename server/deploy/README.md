# Demo-Instanz (systemd-User-Dienst hinter Caddy)

Vorlage für die Demo `https://whisperloom-admin.ctreitges.de` (W2-DEMO); taugt als Muster für jede Installation ohne
Docker. Allgemeine Installation: [`../README.md`](../README.md).

| Datei | Zweck |
|---|---|
| `setup-demo.sh` | venv, Hauptschlüssel und env (600), Datenordner (700), Instanz-Admin `admin`, Firma „Demo GmbH“ mit owner `demo`, Units aktivieren. Wiederholbar. |
| `env.example` | Umgebung (`WL_*`), wird nach `~/.config/whisperloom-admin/env` kopiert |
| `whisperloom-admin.service` | Server auf `127.0.0.1:8099` |
| `whisperloom-admin-fake.service` | Fake-Anbieter `tools/fake_api.py` auf `127.0.0.2:8100` (nur Loopback, ohne Forward-Proxy) |
| `wl-admin` | Admin-CLI mit der Umgebung des Dienstes; dazu `demo-fake-url`, `demo-fake-requests`, `demo-fake-reset` |
| `Caddyfile.block` | Host-Block für die Caddy-Vorlage |

## Ablauf

```sh
git clone https://github.com/CTreitges/whisperloom-desktop ~/whisperloom-admin/app
sh ~/whisperloom-admin/app/server/deploy/setup-demo.sh
curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:8099/healthz      # 200
# Caddy: Caddyfile.block in die Vorlage übernehmen, validieren, installieren, neu laden
```

Zugangsdaten: `~/.config/whisperloom-admin/admin-initial.txt` (600). Der Instanz-Admin richtet TOTP beim ersten Login
ein (Pflicht); Firmen-Admins können es überspringen („Später einrichten“), außer ihre Firma verlangt Zwei-Faktor.

## Sicherheit

- Anmeldearten `password_totp,password` (Zwei-Faktor freiwillig, Instanz-Admin immer mit TOTP), Cookies
  `Secure`/`__Host-` über `WL_BASE_URL=https://…`, Header setzt die App.
- `WL_TRUSTED_PROXIES=127.0.0.1/32`: nur Caddy liefert `X-Forwarded-For` (Rate-Limits je Client). **Grenze
  (Review N-17):** Der Server kann Caddy nicht von anderen lokalen Prozessen unterscheiden. Wer auf dem VPS Code
  ausführt (anderer Dienst, anderer Benutzer), kann `X-Forwarded-For` fälschen und die IP-Limits für Login und
  Enrollment umgehen; die Grenzen je Benutzer, Gerät und Firma bleiben. Die Demo nimmt das hin (Ein-Benutzer-VPS).
  Strenger: Server an eine eigene Loopback-Adresse binden und nur die Quelladresse von Caddy vertrauen, oder Caddy
  und Server in ein eigenes Netz legen.
- Login-Sperre aufheben (z. B. nach eigenen Tippfehlern oder einer TOTP-Sperre):
  `~/whisperloom-admin/app/server/deploy/wl-admin admin-unlock <benutzer>`.
- `WL_UPSTREAM_ALLOW=127.0.0.2:8100`: Der Proxy darf von den internen Zielen nur den Fake auf `127.0.0.2:8100`
  erreichen. Auf `127.0.0.1` lauschen andere Dienste (u. a. die Caddy-Admin-API); eine Freigabe für `127.0.0.1/32` würde
  sie für jeden Firmen-Admin erreichbar machen.
- Freigaben **immer mit Port** (W2-SFIX): Ohne Port gälte die Freigabe für alle Ports der IP, also auch für Dienste,
  die auf allen Adressen lauschen (`0.0.0.0`/`*`: sshd, rpcbind, Dev-Server). Basis-URLs mit `#`, `?`, `@` oder
  Backslash lehnt der Server ab, der Anbieter-Pfad lässt sich nicht abschneiden.
- **Umstellen einer bestehenden Demo:** in `~/.config/whisperloom-admin/env` `WL_UPSTREAM_ALLOW=127.0.0.2:8100` und
  `WL_AUTH_MODES=password_totp,password` setzen, dann `systemctl --user restart whisperloom-admin`.
- **Update:** `git -C ~/whisperloom-admin/app pull --ff-only`, `sh ~/whisperloom-admin/app/server/deploy/setup-demo.sh`
  (installiert venv-Abhängigkeiten und Units neu, lässt env, Schlüssel, Admins und Keys stehen), dann
  `systemctl --user restart whisperloom-admin whisperloom-admin-fake`.
- **Unit-Härtung:** nur seccomp-Optionen (`SystemCallFilter`, `RestrictAddressFamilies`, `MemoryDenyWriteExecute`, …).
  `ProtectSystem`, `PrivateTmp` und `ProtectHome` wirken in User-Units nicht (kein eigener Mount-Namespace; auf Ubuntu
  24.04 geprüft: Dateien in `~` und `/tmp` entstehen trotzdem) und fehlen deshalb bewusst. Mehr Abschottung nur als
  System-Unit mit eigenem Benutzer (siehe `../README.md`).
- Hauptschlüssel außerhalb des Datenordners (`WL_MASTER_KEY_FILE`), ein Backup von `data/` enthält ihn nicht.

## Live-Smoke

```sh
py -3 -X utf8 tools/smoke.py --platform win --suite enroll --id <ID> --work-dir <W> --app <exe> \
  --server https://whisperloom-admin.ctreitges.de --admin-cli "ssh oracle-vps whisperloom-admin/app/server/deploy/wl-admin"
```

Die Suite legt eine eigene Testfirma an und löscht sie am Ende (`tenant-delete`); „Demo GmbH“ bleibt.
