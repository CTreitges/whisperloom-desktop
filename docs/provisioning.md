# Provisioning durch die IT / IT provisioning

[Deutsch](#deutsch) · [English](#english)

## Deutsch

Ziel: Mitarbeitende tippen nie einen API-Key. Die IT verteilt eine Datei; WhisperLoom legt die Keys im
Credential Manager ab, übernimmt die Einstellungen und setzt eine Richtlinie. Format: [settings-format.md](settings-format.md).

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

On Linux the same command stores keys in the Secret Service (via `secret-tool`) or, if none is available, in
`~/.config/whisperloom/secrets.json` with mode 600.

Check: `WhisperLoom status --json` shows `policy` and `keys` (presence only); locked fields are greyed out in the
settings window with the note “set by IT”; `WhisperLoom export <file>` never contains keys or the policy.
