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
Auch „Zugang prüfen“ schickt einen Key nur an einen freigegebenen Server.

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

### Prüfen

- `WhisperLoom status --json` → Abschnitt `policy` (`active`, `locked`, …) und `keys` (nur vorhanden ja/nein).
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

Check: `WhisperLoom status --json` shows `policy` and `keys` (presence only); locked fields are greyed out in the
settings window with the note “set by IT”; `WhisperLoom export <file>` never contains keys or the policy.
