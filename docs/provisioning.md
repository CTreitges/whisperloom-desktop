# Provisioning durch die IT / IT provisioning

[Deutsch](#deutsch) · [English](#english)

## Deutsch

Ziel: Mitarbeitende tippen nie einen API-Key. Die IT verteilt eine Datei; WhisperLoom legt die Keys im
Credential Manager ab, übernimmt die Einstellungen und setzt eine Richtlinie. Format: [settings-format.md](settings-format.md).
Keys sind für den Benutzer auslesbar; nur Keys mit Budget- bzw. Rechtelimit verteilen.

### Ablauf

1. **Datei erstellen** (einmal, zentral), z. B. `whisperloom-it.json`:

   ```json
   {
     "schemaVersion": 1,
     "language": "de",
     "stt": { "providerId": "groq", "model": "whisper-large-v3-turbo" },
     "llm": { "providerId": "openai" },
     "refine": { "stage": "polish" },
     "secrets": { "groq": "<Key>", "openai": "<Key>" },
     "policy": {
       "lockSettings": true,
       "historyEnabled": false,
       "updatesEnabled": false,
       "allowedProviders": ["groq", "openai"]
     }
   }
   ```

   Nur die Felder angeben, die festgelegt werden sollen; alles andere behält der Nutzer bzw. der Standard.
   Mit `"lockSettings": true` sind genau diese Felder und die Keys danach gesperrt. Statt `true` geht eine Liste,
   z. B. `["stt", "llm.providerId"]`.
2. **Verteilen** auf den Rechner, in ein Verzeichnis, das nur der Benutzer lesen kann (z. B. `%LOCALAPPDATA%\Temp`).
3. **Befehl ausführen** im Kontext des Benutzers (nicht als Administrator, die Keys gehören in den Credential Manager
   des Benutzers):

   ```
   "%LOCALAPPDATA%\WhisperLoom\current\WhisperLoom.exe" provision <pfad>\whisperloom-it.json
   ```

   Der Pfad ist der Standard der Installation ohne Admin-Rechte (Velopack, Phase 6); bei anderer Verteilung anpassen.
   Ausgabe: je Zeile `setting <pfad>`, `key <zugang>`, `locked <pfad>`, `policy …` und zuletzt
   `ok provision (Credential Manager)`. Keys erscheinen nie in der Ausgabe. Exit-Code 0 = ok, 1 = Datei ungültig
   (dann wird nichts geschrieben; die Meldungen beginnen mit `error`), 2 = Aufruf falsch.
   Läuft WhisperLoom schon, übernimmt es den neuen Stand sofort.
4. **Datei löschen.** WhisperLoom ändert und löscht die Datei nicht — das macht das Skript (unten).

Wiederholen ist gefahrlos: Ein neues Provisioning setzt die Werte erneut und ersetzt die Richtlinie.

**Grenzen der Sperren:** Sie verhindern Bedienfehler (Fenster, Import, `WhisperLoom stage`, `WhisperLoom key set|delete`),
sind aber keine Sicherheitsgrenze. `settings.json` und `policy.json` liegen im Datenordner des Benutzers; wer dort
schreiben darf (der Benutzer selbst, lokale Admins), kann sie ändern. Für echte Durchsetzung braucht es Mittel des
Betriebssystems (z. B. Rechte auf den Ordner, Softwareverteilung, die das Provisioning regelmäßig wiederholt).
Ist `policy.json` unlesbar, lehnen `WhisperLoom key set|delete` und `update` ab (Exit 1), statt ohne Richtlinie zu laufen.

**Freigabeliste `allowedProviders`:** Sie gilt auch für die Adresse. Bei Anbietern mit fester Adresse (z. B. `groq`,
`openai`) ist eine eigene `baseUrl` zu einem anderen Host ein Verstoß — im Einstellungsfenster wie beim Import — damit
Diktate und der verteilte Key nicht an einen fremden Server gehen. `custom` (eigener Server) und `ollama` haben keine
feste Adresse: Wer sie freigibt, gibt jeden Server frei; dann `stt.baseUrl` bzw. `llm.baseUrl` mitsperren.
Auch „Verbindung testen“ schickt einen Key nur an einen freigegebenen Server.

**Provisioning ist der vertrauenswürdige Weg:** Anders als ein Import löscht `provision` keine vorhandenen Keys des
Benutzers, wenn die Datei Adressen ändert — ein vorhandener Key ginge dann an die neue Adresse. Nur Dateien aus
eigener, vertrauenswürdiger Quelle provisionieren; für Vorlagen von Dritten ist `WhisperLoom import` da.

**Fremde Einstellungsdateien:** Ein Import des Nutzers übernimmt nie Keys, Richtlinie, den Pfad der Vokabular-Datei
oder die Ollama-Adresse. Lenkt er einen gespeicherten Key an einen anderen Host (Adresse, Anbieter oder manueller
Proxy), warnt die Vorschau und der Key wird beim Übernehmen gelöscht. Ein manueller Proxy bekommt nie die
Windows-Anmeldung (NTLM/Kerberos); die gilt nur für den System-Proxy. Verlangt der Firmen-Proxy die Windows-Anmeldung,
ihn als System-Proxy setzen (Interneteinstellungen bzw. PAC per Gruppenrichtlinie) und in WhisperLoom „System“ lassen.

### Beispielskript (PowerShell, z. B. als Anmeldeskript oder per Softwareverteilung)

```powershell
# whisperloom-provision.ps1 — läuft als der angemeldete Benutzer.
param(
    [Parameter(Mandatory)] [string] $File,
    [string] $Exe = "$env:LOCALAPPDATA\WhisperLoom\current\WhisperLoom.exe"
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Exe)) { throw "WhisperLoom nicht gefunden: $Exe" }
try {
    $out = & $Exe provision $File 2>&1
    $code = $LASTEXITCODE
    # Ausgabe enthält nur Pfade und Zugangsnamen, keine Keys: darf ins Log.
    $out | ForEach-Object { Write-Output "whisperloom: $_" }
    if ($code -ne 0) { throw "provision fehlgeschlagen (Exit $code)" }
}
finally {
    # Die Datei enthält Keys: immer löschen, auch bei Fehler.
    Remove-Item -LiteralPath $File -Force -ErrorAction SilentlyContinue
}
```

### Linux

Gleicher Befehl: `WhisperLoom provision <datei>`. Keys landen im Secret Service (GNOME-Schlüsselbund, KWallet über
`secret-tool`); ist keiner erreichbar, in `~/.config/whisperloom/secrets.json` mit Rechten 600. Danach die Datei
löschen (`shred -u <datei>` bzw. `rm`).

### Drei Stufen in der Datei

Statt `lockSettings` (alles Fest) geht je Pfad eine Stufe, wie beim Firmen-Server: `"policy": {"levels": {"stt":
"fixed", "refine.stage": "default", "polish": "free"}}`. **Fest** ist in der App ausgeblendet, **Vorgabe** darf der
Nutzer ändern (seine Änderung gewinnt bis „Auf Firmenvorgabe zurücksetzen“), **Frei** setzt nur einmal. Tastenkürzel
sind nie Fest. Details: [settings-format.md](settings-format.md#richtlinie-drei-stufen). Die App merkt sich die Werte
der festen Pfade (`policy-fixed.json`) und setzt sie beim Start wieder ein, falls `settings.json` von Hand geändert wurde.

### Firmen-Server und Software-Verteilung (W2-ENR)

Mit einem Firmen-Server (`server/`, [server-api.md](server-api.md)) meldet sich jedes Gerät einmal mit dem
**Firmencode** an; danach steuert die Firma Modelle, Anbieter, Server und Einstellungen über das Portal, und die App
gleicht beim Start, alle 15 Minuten und auf „Jetzt abgleichen“ ab. Drei Wege, alle ohne Admin-Rechte:

1. **Assistent**: „Mit meiner Firma“ → Server-Adresse und Firmencode → „Wartet auf Freigabe durch die IT“ → nach der
   Freigabe geht es von selbst weiter. Ein **Einladungslink** `whisperloom://join?server=<url>&code=<code>` füllt beide
   Felder (Windows: Schema je Benutzer unter `HKCU\Software\Classes\whisperloom`, das die App beim Start einträgt;
   Linux: `~/.local/share/applications/whisperloom-join.desktop` + `xdg-mime`).
2. **Befehl** (Softwareverteilung, Anmeldeskript), im Kontext des Benutzers:

   ```
   WhisperLoom.exe join --server https://wl.example.com --code ABCD-EFGH-JKMN-PQRS [--device-name PC-Buchhaltung-3]
   ```

   Ausgabe `ok join state=pending|approved device=d_… tenant=<Firma> sync=…`. Exit-Code 0 = verbunden (wartet auf
   Freigabe bzw. freigegeben, dann schon abgeglichen), 1 = Fehler (`error <code>: <Text>`, z. B. `code_invalid`,
   `offline`), 2 = Aufruf falsch. Ohne `--device-name` meldet die App den Rechnernamen. Code und Token erscheinen nie in
   der Ausgabe; das Geräte-Token liegt im Credential Manager unter `WhisperLoom:device`.
3. **Provisioning-Datei** mit `"management": {"serverUrl": "https://wl.example.com", "code": "ABCD-EFGH-JKMN-PQRS"}`:
   `provision` speichert beides, der **erste Start** verbindet und verwirft den Code. Weitere Felder der Datei gelten
   wie bisher.

Was danach gilt:

- **Proxy-Modus** (Standard): Keys bleiben auf dem Server; Erkennung und Text laufen über `<server>/api/v1/proxy`, die
  App authentifiziert sich mit dem Geräte-Token. Ein aufgebrauchtes Budget lehnt das Diktat ab, ohne Ersatz-Anbieter: „Firmenbudget für diesen Monat erreicht – bitte IT fragen“.
- **Verteilt-Modus**: Die Keys der Firma landen im Credential Manager unter `WhisperLoom:company:<zugang>`, getrennt
  von eigenen Keys des Nutzers; Sperre und „Firma verlassen“ löschen sie.
- **Offline** gilt die letzte Konfiguration weiter, Fest bleibt Fest. **Gesperrt** (Portal) schaltet die
  Firmen-Zugänge ab und zeigt einen Hinweis; eigene Einstellungen bleiben. Ein später gesendetes `provision` oder ein
  Import ändern Fest des Servers nicht.
- „Firma verlassen“ (Einstellungen → Firma) nur, wenn die Firma `management.allowLeave` nicht Fest auf `false` setzt.

**Microsoft Intune** (Win32-App bzw. Plattformskript, „Run this script using the logged on credentials: Yes“):

```powershell
# whisperloom-join.ps1 — Intune-Plattformskript im Benutzerkontext.
$exe = "$env:LOCALAPPDATA\WhisperLoom\current\WhisperLoom.exe"
if (-not (Test-Path $exe)) { exit 1 }   # App zuerst als Win32-App (Velopack-Setup) verteilen
$out = & $exe join --server "https://wl.example.com" --code "ABCD-EFGH-JKMN-PQRS" 2>&1
$out | ForEach-Object { Write-Output "whisperloom: $_" }   # ohne Code und Token: darf ins Log
exit $LASTEXITCODE
```

Erkennungsregel für die Win32-App: Registry `HKCU\Software\Classes\whisperloom` vorhanden (legt die App beim ersten
Start an) oder Datei `%APPDATA%\WhisperLoom\management-cache.json` vorhanden (nach dem ersten Abgleich).

**Gruppenrichtlinie** (Benutzerkonfiguration → Windows-Einstellungen → Skripts → Anmelden): dasselbe Skript; ein
erneuter Lauf auf einem schon verbundenen Gerät meldet nur ein weiteres Gerät an — deshalb vorher prüfen:

```powershell
$status = & $exe status --json | ConvertFrom-Json
if ($status.management.state -in 'pending','approved') { exit 0 }
```

(`status` braucht eine laufende App; ohne sie prüft das Skript `%APPDATA%\WhisperLoom\settings.json` →
`management.state`.)

**Linux** (Skript, z. B. per Ansible als der Benutzer): `WhisperLoom join --server https://wl.example.com --code …`;
das Token landet im Secret Service bzw. in `~/.config/whisperloom/secrets.json` (600).

### Prüfen

- `WhisperLoom status --json` → Abschnitt `policy` (`active`, `locked`, …) und `keys` (nur vorhanden ja/nein);
  mit Firmen-Server zusätzlich `management` (`state`, `tenantName`, `lastSync`, `keyMode`, `notice`, nie das Token).
- `WhisperLoom sync` gleicht sofort ab (laufende App; Ausgabe `sync=synced|notmodified|pending|blocked|offline|…`).
- Im Einstellungsfenster sind gesperrte Felder ausgegraut mit dem Hinweis „von IT festgelegt“.
- `WhisperLoom export <datei>` enthält nie Keys und nie die Richtlinie.

## English

Goal: staff never type an API key. IT distributes a file; WhisperLoom stores the keys in Credential Manager, applies
the settings and sets a policy. Format: [settings-format.md](settings-format.md) (German).
Keys can be read by the user; only distribute keys with a budget or permission limit.

### Steps

1. **Create the file** once, e.g. `whisperloom-it.json` (same example as above). List only the fields you want to
   set; everything else stays as the user had it. `"lockSettings": true` locks exactly those fields and the keys;
   a list such as `["stt", "llm.providerId"]` locks just those paths. `secrets` maps a provider ID (key used for every
   access with that provider) or an access name (`stt`, `stt_fallback`, `llm`, `llm_fallback`, takes precedence) to the key.
   `historyEnabled` forces history on/off, `updatesEnabled: false` turns auto-updates off, `allowedProviders`
   limits which providers users may pick.
2. **Distribute** it to a folder only the user can read.
3. **Run** as the signed-in user (not as admin; keys belong in the user's Credential Manager):
   `WhisperLoom.exe provision <path>\whisperloom-it.json`. Output lists `setting`, `key`, `locked` and `policy` lines and
   ends with `ok provision (Credential Manager)`; keys are never printed. Exit code 0 = ok, 1 = invalid file (nothing
   is written; messages start with `error`), 2 = wrong usage. A running WhisperLoom reloads immediately.
4. **Delete the file.** WhisperLoom never modifies or deletes it — the script does (see the PowerShell example above;
   it deletes the file in a `finally` block, even on failure).

Running it again is safe: values are set again and the policy is replaced.

**Limits of the locks:** they prevent mistakes (settings window, import, `WhisperLoom stage`, `WhisperLoom key set|delete`)
but are not a security boundary. `settings.json` and `policy.json` live in the user's data folder; anyone who can
write there (the user, local admins) can change them. Real enforcement needs OS means (folder permissions, software
distribution that re-runs the provisioning regularly).
If `policy.json` cannot be read, `WhisperLoom key set|delete` and `update` refuse (exit 1) instead of running without a policy.

**Allow list `allowedProviders`:** it also covers the address. For providers with a fixed address (e.g. `groq`,
`openai`) a custom `baseUrl` pointing to another host is a violation — in the settings window and on import — so
dictations and the distributed key never go to a foreign server. `custom` (own server) and `ollama` have no fixed
address: allowing them allows any server; lock `stt.baseUrl` or `llm.baseUrl` as well. "Check access" also sends a
key only to an allowed server.

**Provisioning is the trusted path:** unlike an import, `provision` does not delete existing user keys when the file
changes addresses — an existing key would then go to the new address. Only provision files from your own trusted
source; third-party templates belong in `WhisperLoom import`.

**Foreign settings files:** a user import never takes keys, the policy, the vocabulary file path or the Ollama
address. If it would send a stored key to another host (address, provider or manual proxy), the preview warns and the
key is deleted on apply. A manual proxy never gets the Windows sign-in (NTLM/Kerberos); that only applies to the system
proxy. If the company proxy requires the Windows sign-in, set it as system proxy (Internet settings or PAC via group
policy) and keep WhisperLoom on "System".

On Linux the same command stores keys in the Secret Service (via `secret-tool`) or, if none is available, in
`~/.config/whisperloom/secrets.json` with mode 600.

**Three levels in the file:** instead of `lockSettings` (everything fixed) set a level per path, as on the company
server: `"policy": {"levels": {"stt": "fixed", "refine.stage": "default", "polish": "free"}}`. *Fixed* is hidden in the
app, *default* may be changed by the user (their change wins until “Reset to company default”), *free* is set once.
Shortcuts are never fixed. The app remembers the values of fixed paths (`policy-fixed.json`) and restores them at start
if `settings.json` was edited by hand.

### Company server and software distribution (W2-ENR)

With a company server (`server/`, [server-api.md](server-api.md)) each device signs in once with the **company code**;
afterwards the company controls models, providers, servers and settings through the portal, and the app syncs at
start, every 15 minutes and on “Sync now”. Three ways, none needs admin rights:

1. **Setup wizard:** “With my company” → server address and company code → “Waiting for approval by your IT team” →
   continues on its own after approval. An **invitation link** `whisperloom://join?server=<url>&code=<code>` fills both
   fields (Windows: per-user scheme under `HKCU\Software\Classes\whisperloom`, registered by the app at start; Linux:
   `~/.local/share/applications/whisperloom-join.desktop` + `xdg-mime`).
2. **Command** (software distribution, logon script), run as the user:
   `WhisperLoom.exe join --server https://wl.example.com --code ABCD-EFGH-JKMN-PQRS [--device-name PC-3]`.
   Output `ok join state=pending|approved device=d_… tenant=<company> sync=…`. Exit code 0 = connected (waiting for
   approval or approved and already synced), 1 = error (`error <code>: <text>`), 2 = wrong usage. Code and token are
   never printed; the device token lives in Credential Manager as `WhisperLoom:device`.
3. **Provisioning file** with `"management": {"serverUrl": "…", "code": "…"}`: `provision` stores both, the **first
   start** connects and discards the code.

Afterwards: in **proxy mode** (default) keys stay on the server and the app authenticates against
`<server>/api/v1/proxy` with the device token; an exhausted budget rejects the dictation without any fallback provider: “Company budget for this month reached – please ask your IT team”. In
**distribute mode** company keys go to Credential Manager as `WhisperLoom:company:<access>`, separate from the user's
own keys, and are deleted when the device is blocked or leaves. **Offline**, the last configuration stays in effect
and fixed stays fixed. **Blocked** turns company access off with a notice; the user's own settings stay. A later
`provision` or import never changes the server's fixed values. “Leave company” only when the company does not fix
`management.allowLeave` to `false`.

**Microsoft Intune / group policy:** run the PowerShell example from the German section (`whisperloom-join.ps1`) in
the user context (“Run this script using the logged on credentials: Yes” or a user logon script). Detection rule:
registry key `HKCU\Software\Classes\whisperloom` or file `%APPDATA%\WhisperLoom\management-cache.json`. Skip devices
that are already connected (`status --json` → `management.state` is `pending` or `approved`).

Check: `WhisperLoom status --json` shows `policy`, `keys` (presence only) and, with a company server, `management`
(state, company, last sync, never the token); `WhisperLoom sync` syncs right away. Locked fields are hidden in the
settings window; `WhisperLoom export <file>` never contains keys or the policy.
