# Proxy

WhisperLoom spricht nur HTTPS mit den Anbietern (Erkennung, Textverbesserung) und lädt Modelle bzw. den
Begleitprozess von Hugging Face und GitHub. Alles läuft über einen gemeinsamen HTTP-Client von .NET; welcher
Proxy gilt, stellst du unter **Einstellungen → Netzwerk → Proxy** ein. Lokale Dienste (whisper-server auf
127.0.0.1, Ollama) gehen nie über einen Proxy.

*English summary at the end.*

## Die drei Modi

| Modus | Einstellung | Wirkung |
|---|---|---|
| **System** (Standard) | `desktop.proxyMode = "system"` | Windows: Umgebungsvariablen, sonst die Proxy-Einstellungen des Benutzers (inkl. PAC/automatische Erkennung). Linux: nur Umgebungsvariablen. |
| **Aus** | `"off"` | direkt, ohne Proxy |
| **Manuell** | `"manual"` + `desktop.proxyUrl` | genau diese Adresse, z. B. `http://proxy.firma.local:3128` |

Eine manuelle Adresse, die keine vollständige URL ist (ohne `http://`), gilt als **System**. Per IT-Datei lassen
sich beide Felder setzen und sperren (`docs/provisioning.md`, Pfade `desktop.proxyMode`, `desktop.proxyUrl`).

Der Download-Client (Modelle, whisper-server) liest den Proxy beim App-Start. Nach einer Änderung die App einmal
beenden und neu starten; Erkennung und Text übernehmen ihn sofort.

## Windows

### System-Proxy und PAC
Im Modus **System** fragt .NET zuerst die Umgebungsvariablen (`HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`,
`NO_PROXY`) ab. Sind sie nicht gesetzt, gelten die Proxy-Einstellungen des angemeldeten Benutzers – also das, was
unter *Einstellungen → Netzwerk und Internet → Proxy* steht bzw. per Gruppenrichtlinie verteilt ist:

- **Automatische Erkennung** (WPAD) und **Setupskript verwenden** (PAC-Datei, `http://…/proxy.pac`) werden
  ausgewertet; die PAC-Datei entscheidet je Ziel-Host.
- **Manuelle Proxyeinrichtung** samt Ausnahmen („Proxy nicht für lokale Adressen verwenden“, Ausnahmeliste) gilt
  ebenso.

Für Firmen-Laptops ist deshalb meist nichts einzustellen: Was der Browser (Edge/Chrome) nutzt, nutzt WhisperLoom auch.

### Anmeldung am Proxy: NTLM und Kerberos
WhisperLoom meldet sich beim Proxy mit dem **angemeldeten Windows-Konto** an (`CredentialCache.DefaultCredentials`).
Verlangt der Proxy `Negotiate` (Kerberos) oder `NTLM`, läuft das ohne Passwort-Eingabe – wie im Browser. Es wird
kein Proxy-Passwort gespeichert, und es gibt keines in den Einstellungen.

Proxys mit **Basic-Anmeldung** (Benutzername/Passwort) unterstützt die Oberfläche bewusst nicht. Notlösung: die
Umgebungsvariable `HTTPS_PROXY=http://benutzer:passwort@proxy:3128` setzen und den Modus **System** lassen. Das
Passwort steht dann im Klartext in der Umgebung des Benutzers – nur in Absprache mit der IT.

### Prüfen
1. Einstellungen → Erkennung → **Zugang prüfen**. „Zugang funktioniert“ heißt: Proxy und Key passen.
2. Meldet die Prüfung „Netzwerkfehler“ oder HTTP 407 (Proxy-Anmeldung verlangt):
   - Modus **Manuell** mit der Adresse aus der PAC-Datei versuchen;
   - prüfen, ob die IT den Anbieter-Host freigegeben hat (Liste unten);
   - im Log (`%APPDATA%\WhisperLoom\logs`) steht nur die Fehlerklasse, nie Key oder Text.

## Linux

.NET liest unter Linux **keine** Proxy-Einstellungen von GNOME oder KDE und keine PAC-Dateien, nur die
Umgebungsvariablen. Im Modus **System** gelten:

| Variable | Bedeutung |
|---|---|
| `HTTPS_PROXY` / `https_proxy` | Proxy für HTTPS (alle Anbieter) |
| `HTTP_PROXY` / `http_proxy` | Proxy für HTTP |
| `ALL_PROXY` / `all_proxy` | Rückfall für beide |
| `NO_PROXY` / `no_proxy` | Ausnahmen, kommagetrennt; `.firma.local` mit führendem Punkt für Subdomains, keine `*` |

Kleinschreibung wird zuerst gelesen. Erlaubt sind `http://`, `https://`, `socks4://`, `socks4a://`, `socks5://`.

Damit die Variablen auch für den Autostart und für Tastenkürzel gelten (die nicht aus einer Shell starten), gehören
sie in die Sitzungsumgebung, z. B. für systemd-basierte Sitzungen (KDE Plasma, GNOME):

```ini
# ~/.config/environment.d/50-proxy.conf
HTTPS_PROXY=http://proxy.firma.local:3128
NO_PROXY=localhost,127.0.0.1,.firma.local
```

Danach ab- und wieder anmelden. Alternativ in WhisperLoom den Modus **Manuell** wählen – das wirkt unabhängig von
der Umgebung.

Kerberos am Proxy ist unter Linux möglich, wenn ein gültiges Ticket vorliegt (`kinit`) und die GSSAPI-Bibliothek
installiert ist (`krb5`); das haben wir nicht getestet. NTLM unter Linux wird nicht unterstützt.

## Freizugebende Hosts (Firewall/Proxy)

| Zweck | Host |
|---|---|
| Erkennung/Text je nach Anbieter | `api.openai.com`, `api.groq.com`, `api.anthropic.com`, `api.mistral.ai`, `api.elevenlabs.io`, `generativelanguage.googleapis.com`, `openrouter.ai`, `api.deepseek.com`, `api.together.ai`, `api.deepinfra.com`, `ollama.com` |
| Lokale Modelle | `huggingface.co` (leitet auf dessen CDN weiter) |
| Begleitprozess whisper-server, Updates | `github.com` und `objects.githubusercontent.com` / `release-assets.githubusercontent.com` |

Nur die Hosts der tatsächlich genutzten Anbieter sind nötig. Mit `allowedProviders` in der IT-Datei lässt sich die
Auswahl in der App passend einschränken.

## English summary

- **Settings → Network → Proxy**: *System* (default), *Off*, or *Manual* (`http://host:port`). Local services
  (whisper-server, Ollama) never use a proxy. Restart the app after changing the proxy so model downloads pick it up.
- **Windows, System mode**: environment variables first, otherwise the user's proxy settings, including PAC scripts and
  automatic detection (WPAD). Proxy authentication via **NTLM/Kerberos uses the signed-in Windows account** — no
  password is stored. Basic auth only through `HTTPS_PROXY=http://user:pass@host:port` (plain text, ask your IT).
- **Linux**: only environment variables (`HTTPS_PROXY`, `HTTP_PROXY`, `ALL_PROXY`, `NO_PROXY`, lower case checked
  first); GNOME/KDE proxy settings and PAC files are not read. Put them in `~/.config/environment.d/*.conf` so autostart
  and desktop shortcuts see them, or use *Manual* mode.
- Hosts to allow are listed in the table above.

<!-- Doku: learn.microsoft.com/dotnet/api/system.net.http.httpclient.defaultproxy (abgerufen 2026-11-01). -->
