# WhisperLoom Desktop

Diktieren mit KI-Textstufen für Windows und Linux – der Kern von [WhisperLoom für Android](https://github.com/CTreitges/whisperloom-android)
als Hintergrund-App. **[English version below](#english).**

---

## Deutsch

### Was es kann
- **Diktat → Erkennung → Text-Stufe → Ausgabe**, im Hintergrund mit Tray-Symbol.
- **Erkennung** in der Cloud (OpenAI, Groq, Mistral, ElevenLabs, eigener OpenAI-kompatibler Server u. a., eigener Key) oder **lokal** mit
  whisper.cpp (Vulkan-GPU oder CPU, ohne Internet).
- **Stufen:** Aus (Rohtext), Glätten, Verschönern, Zusammenfassen, Lesbar, **Prompt** (macht aus einem Diktat einen sauberen
  Prompt für KI-Assistenten). Textverbesserung über den Anbieter (z. B. Claude, Gemini, OpenAI) oder lokal über Ollama.
- Füllwörter entfernen, Absätze, **Kontext-Wörter** (Namen, Fachbegriffe), Fallback-Anbieter.
- **Windows:** frei belegbare Tastenkürzel – *Halten* (Standard Strg+Win), *Umschalten* mit Overlay
  (Strg+Leertaste; Enter = einfügen, Strg+Enter = einfügen + Enter, Rücktaste = verwerfen) und *Stufe wechseln*
  (Strg+Alt+Leertaste). Der Text wird ins aktive Fenster eingefügt oder direkt getippt.
- **Windows-Extras:** die letzten 10 Diktate im Tray, *letztes erneut einfügen* (Strg+Alt+Umschalt+V),
  *neu verarbeiten* aus dem Rohtext mit anderer Stufe, **App-Profile** (z. B. Windows Terminal → Stufe Prompt,
  Outlook → Verschönern) und **markierten Text per Sprachanweisung umschreiben** (Strg+Alt+Umschalt+R).
- **Linux (Wayland/X11):** Befehl `WhisperLoom toggle` für ein Kürzel der Desktop-Umgebung, Ergebnis in der
  Zwischenablage.
- **Bühnenansicht** für Beamer: Rohtext neben dem Ergebnis in Großschrift.
- **Verlauf** (abschaltbar), Einstellungen DE/EN, Export/Import kompatibel mit Android.

### Datenschutz
- API-Keys liegen nur im Schlüsselbund des Systems (Windows-Anmeldeinformationsverwaltung, Linux Secret Service bzw.
  eine Datei mit Rechten 600) – nie in `settings.json`, nie im Export, nie im Log. In der Oberfläche sind sie immer
  maskiert.
- **Audio kommt nie auf die Platte.** Diktattext nur im Verlauf, wenn der eingeschaltet ist.
- Logs enthalten nur Metadaten (Dauer, Anbieter, Status, Fehlerklasse).

### Installation
Alle Pakete liegen in den [GitHub-Releases](https://github.com/CTreitges/whisperloom-desktop/releases):

| Datei | Für | Updates |
|---|---|---|
| `WhisperLoom-win-Setup.exe` | Windows, Installation pro Benutzer nach `%LocalAppData%\WhisperLoom`, ohne Admin-Rechte | ja: Prüfung beim Start und täglich, Installation erst nach Bestätigung |
| `WhisperLoom-<version>-win-x64-portable.zip` | Windows ohne Installation (entpacken, `WhisperLoom.exe` starten), z. B. für Firmen-Verteilung | nein |
| `WhisperLoom.AppImage` | Linux (`chmod +x WhisperLoom.AppImage`, dann starten) | ja |

Die Builds sind **unsigniert**; Windows SmartScreen fragt beim ersten Start nach („Weitere Informationen“ →
„Trotzdem ausführen“). Firmen signieren selbst: `docs/signing.md`. Updates lassen sich per IT-Richtlinie abschalten
(`updatesEnabled=false`). Version anzeigen: `WhisperLoom --version`.

Empfohlene Linux-Pakete: `wl-clipboard` (Wayland) bzw. `xclip` (X11), `libsecret` für den Schlüsselbund. Details:
[`docs/DEMO.md`](docs/DEMO.md).

### Erster Start: Einrichtungs-Assistent
Beim ersten Start führt ein Assistent in wenigen Schritten zum ersten Diktat:

1. **Sprache** der Oberfläche und des Diktats
2. **Mikrofon** wählen, mit Pegel-Test
3. **Erkennung:** Cloud (Anbieter + Key, „Zugang prüfen“) oder Lokal (Modell laden)
4. **Stufe** wählen
5. **Tastenkürzel:** Windows – Kombi testen; Linux – Anleitung fürs Desktop-Kürzel
6. **Autostart** beim Anmelden (nur für den eigenen Benutzer)
7. **Fertig**

Überspringen geht jederzeit; erneut aufrufen über Tray → **Einrichtung …** oder `WhisperLoom setup`. Hat die IT
Einstellungen vorgegeben, entfallen die gesperrten Schritte.

### Bedienung
| | Windows | Linux |
|---|---|---|
| Diktat starten/stoppen | Strg+Win halten oder Strg+Leertaste | Kürzel auf `WhisperLoom toggle` ([Anleitung](docs/linux-shortcuts.md)) |
| Stufe wechseln | Strg+Alt+Leertaste oder Tray | `WhisperLoom stage next` oder Tray |
| Ergebnis | ins aktive Fenster eingefügt | Zwischenablage |
| Letztes erneut einfügen | Strg+Alt+Umschalt+V oder Tray → Letzte Diktate | `WhisperLoom reinsert` |
| Markierung umschreiben | Strg+Alt+Umschalt+R, dann Anweisung sprechen | – |

Weitere Befehle: `start`, `stop`, `cancel`, `pause`, `resume`, `show`, `setup`, `status --json`, `reprocess <stufe>`,
`recent-insert <n>`, `update`, `--version`, `quit`.

### Lokale Modelle und eigene Anbieter
Eigene Whisper-Dateien, lokale Server (LM Studio, llama.cpp, faster-whisper-server), mitgeliefertes Textmodell über
llama.cpp und eigene Anbieter-Einträge: [`docs/local-models.md`](docs/local-models.md).

### Firmen-Einsatz
- **Proxy:** System-Proxy inkl. PAC und NTLM/Kerberos mit dem Windows-Konto, manuell oder aus – [`docs/proxy.md`](docs/proxy.md).
- **IT-Vorgaben** (Anbieter, Keys, gesperrte Felder, Verlauf, Updates): [`docs/provisioning.md`](docs/provisioning.md),
  Format: [`docs/settings-format.md`](docs/settings-format.md).
- **Signatur** der Builds durch die Firma, Verteilung der portablen ZIP: [`docs/signing.md`](docs/signing.md).

### Aus dem Quellcode bauen
Voraussetzung: .NET SDK 10 (`global.json`).

```bash
dotnet build WhisperLoom.slnx -c Release
dotnet test WhisperLoom.slnx
dotnet run --project src/WhisperLoom.App
```

Aufbau: `src/WhisperLoom.Core` (Fachlogik, OS-frei), `src/WhisperLoom.App` (Avalonia-App), `tests/`, `tools/`
(Paritäts-Checks, Fake-API, Smokes). Schnittstellen: [`docs/contracts.md`](docs/contracts.md).

### Lizenz
MIT, siehe [`LICENSE`](LICENSE). Komponenten von Dritten: [`THIRD_PARTY.md`](THIRD_PARTY.md). Änderungen:
[`CHANGELOG.md`](CHANGELOG.md).

---

## English

### What it does
- **Dictation → recognition → text stage → output**, running in the background with a tray icon.
- **Recognition** in the cloud (OpenAI, Groq, Mistral, ElevenLabs, your own OpenAI-compatible server and more, your own key) or **locally** with
  whisper.cpp (Vulkan GPU or CPU, no internet).
- **Stages:** Off (raw text), Polish, Beautify, Summarize, Readable, **Prompt** (turns a dictation into a clean prompt for AI
  assistants). Text refinement through the provider (e.g. Claude, Gemini, OpenAI) or locally through Ollama, a bundled
  llama.cpp model or a local server; own Whisper files and custom providers: [`docs/local-models.md`](docs/local-models.md).
- Filler-word removal, paragraphs, **context words** (names, jargon), fallback providers.
- **Windows:** configurable shortcuts – *Hold* (default Ctrl+Win), *Toggle* with an overlay (Ctrl+Space; Enter = insert,
  Ctrl+Enter = insert + Enter, Backspace = discard) and *Next stage* (Ctrl+Alt+Space). Text is pasted into the active
  window or typed directly.
- **Windows extras:** last 10 dictations in the tray, *insert last again* (Ctrl+Alt+Shift+V), *reprocess* from the raw
  text with another stage, **app profiles** (e.g. Windows Terminal → Prompt stage, Outlook → Beautify) and **rewrite the
  selection by voice instruction** (Ctrl+Alt+Shift+R).
- **Linux (Wayland/X11):** command `WhisperLoom toggle` for a desktop-environment shortcut; result on the clipboard.
- **Stage view** for projectors: raw text next to the result in large type.
- **History** (can be turned off), UI in German and English, settings export/import compatible with Android.

### Privacy
- API keys live only in the OS keyring (Windows Credential Manager, Linux Secret Service or a mode-600 file) – never in
  `settings.json`, exports or logs, and always masked in the UI.
- **Audio never touches the disk.** Dictated text is stored only in the history, if enabled.
- Logs contain metadata only (duration, provider, status, error class).

### Install
All packages are on [GitHub Releases](https://github.com/CTreitges/whisperloom-desktop/releases):

| File | For | Updates |
|---|---|---|
| `WhisperLoom-win-Setup.exe` | Windows, per-user install to `%LocalAppData%\WhisperLoom`, no admin rights | yes: checked at start and daily, installed only after you confirm |
| `WhisperLoom-<version>-win-x64-portable.zip` | Windows without installing (unzip, run `WhisperLoom.exe`), e.g. for company rollout | no |
| `WhisperLoom.AppImage` | Linux (`chmod +x WhisperLoom.AppImage`, then run it) | yes |

Builds are **unsigned**; Windows SmartScreen asks on first start (“More info” → “Run anyway”). Companies sign them
themselves: `docs/signing.md`. Updates can be turned off by IT policy (`updatesEnabled=false`). Show the version with
`WhisperLoom --version`.

Recommended Linux packages: `wl-clipboard` (Wayland) or `xclip` (X11), `libsecret` for the keyring. Details:
[`docs/DEMO.md`](docs/DEMO.md).

### First start: setup wizard
On first start a wizard takes you to your first dictation: **language**, **microphone** with a level test,
**recognition** (cloud with key and *Check access*, or local with model download), **stage**, **shortcuts** (Windows:
test the combo; Linux: how to bind the desktop shortcut), **autostart** at sign-in (current user only), **done**.
Skip it any time; run it again from tray → **Setup …** or with `WhisperLoom setup`. Steps locked by your IT are left out.

### Usage
| | Windows | Linux |
|---|---|---|
| Start/stop dictation | hold Ctrl+Win or press Ctrl+Space | shortcut bound to `WhisperLoom toggle` ([guide](docs/linux-shortcuts.md)) |
| Next stage | Ctrl+Alt+Space or tray | `WhisperLoom stage next` or tray |
| Result | pasted into the active window | clipboard |
| Insert last again | Ctrl+Alt+Shift+V or tray → Recent dictations | `WhisperLoom reinsert` |
| Rewrite the selection | Ctrl+Alt+Shift+R, then speak the instruction | – |

More verbs: `start`, `stop`, `cancel`, `pause`, `resume`, `show`, `setup`, `status --json`, `reprocess <stage>`,
`recent-insert <n>`, `update`, `--version`, `quit`.

### In companies
- **Proxy:** system proxy incl. PAC and NTLM/Kerberos with the Windows account, manual, or off – [`docs/proxy.md`](docs/proxy.md).
- **IT provisioning** (providers, keys, locked fields, history, updates): [`docs/provisioning.md`](docs/provisioning.md),
  format: [`docs/settings-format.md`](docs/settings-format.md).
- **Code signing** by the company, rolling out the portable ZIP: [`docs/signing.md`](docs/signing.md).

### Build from source
Requires the .NET 10 SDK (`global.json`).

```bash
dotnet build WhisperLoom.slnx -c Release
dotnet test WhisperLoom.slnx
dotnet run --project src/WhisperLoom.App
```

Layout: `src/WhisperLoom.Core` (OS-independent logic), `src/WhisperLoom.App` (Avalonia app), `tests/`, `tools/`
(parity checks, fake API, smoke tests). Interfaces: [`docs/contracts.md`](docs/contracts.md).

### License
MIT, see [`LICENSE`](LICENSE). Third-party components: [`THIRD_PARTY.md`](THIRD_PARTY.md). Changes:
[`CHANGELOG.md`](CHANGELOG.md).
