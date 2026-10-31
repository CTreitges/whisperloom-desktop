# WhisperLoom Desktop — Demo guide (Manjaro / Linux, also Windows)

This guide gets WhisperLoom running for a live demo: dictate in English, let the **Prompt** stage turn the
dictation into a clean prompt, and show raw text and result side by side on a projector.

> Status (demo build): online recognition and text refinement work end to end — the same providers, stages
> and prompts as WhisperLoom for Android. Local recognition (whisper.cpp) and local refinement (Ollama) come in a
> later build; for the demo you need two online accesses: one for **recognition** and one for **text** (Claude).

**Before the demo, have ready:**

- a key for speech recognition — Claude cannot transcribe audio. Easiest: **Groq** (free tier, fast) or **OpenAI**;
  Mistral (Voxtral) and ElevenLabs (Scribe) work too;
- the Claude key for the **Prompt** stage;
- a microphone that works in the desktop's sound settings.

## 1. Download and start

1. Download `linux-x64.tar.gz` from the latest CI run (GitHub → Actions → CI → artifacts) or the demo pre-release.
2. Unpack and start:

   ```bash
   mkdir -p ~/whisperloom && tar -xzf linux-x64.tar.gz -C ~/whisperloom
   ~/whisperloom/WhisperLoom &
   ```

   The app lives in the tray (StatusNotifierItem). If your desktop has no tray, it opens the stage view instead.
   On Windows unzip `win-x64.zip` and run `WhisperLoom.exe` (no admin rights needed).

### Dependencies (Manjaro)

| Purpose | Package | Needed? |
|---|---|---|
| Microphone (ALSA/PipeWire via PortAudio) | `alsa-lib` (`pipewire-alsa` on PipeWire systems) | yes, usually installed |
| Clipboard on Wayland | `wl-clipboard` | yes on Wayland |
| Clipboard on X11 | `xclip` | yes on X11 |
| Keyring for API keys | `libsecret` (`secret-tool`) + GNOME Keyring or KWallet's Secret Service | recommended |
| Tray on GNOME | the AppIndicator/KStatusNotifierItem extension | optional |

```bash
sudo pacman -S --needed alsa-lib wl-clipboard xclip libsecret
```

Without a Secret Service, keys go to `~/.config/whisperloom/secrets.json` with mode 600; the settings show a hint.

## 2. Enter the keys

Tray → **Settings**:

1. **Recognition** tab: **Provider** `Groq` (or `OpenAI`, …), **API key** → **Replace** → paste → **Check access**.
   **Language** `EN` (English recognition; `Automatic` also works). **Model** can stay empty (recommended model).
2. **Text** tab:
   - tick **Offer the “Prompt” stage**, then **Stage** `Prompt`;
   - **Access for text refinement:** `Anthropic (Claude)` → **API key** → **Replace** → paste the Claude key →
     **Check access**. **Model** empty = the recommended Claude model for the stage.
3. Press **Save** at the bottom.

Keys are always masked; there is no way to reveal them, only **Replace**, **Delete** and **Check access**. They are
stored in the OS keyring (Windows Credential Manager, Linux Secret Service) — never in `settings.json`.

Optional: **Fallback** — a second recognition provider that steps in when the first one fails (e.g. Groq first,
OpenAI as fallback). **Network** → **Proxy**: `System` (default), `Off` or `Manual` with an address.

## 3. Keyboard shortcut (Wayland has no global hotkeys)

Bind a desktop shortcut to the CLI command. A second call talks to the running app and exits.

- **KDE Plasma:** System Settings → Shortcuts → *Add New* → *Command or Script* →
  `~/whisperloom/WhisperLoom toggle` → assign e.g. `Meta+Space`.
- **GNOME:** Settings → Keyboard → *View and Customize Shortcuts* → *Custom Shortcuts* → `+` →
  command `~/whisperloom/WhisperLoom toggle`.

Other commands:

| Command | Effect |
|---|---|
| `WhisperLoom toggle` | start recording / stop and process |
| `WhisperLoom start` · `stop` · `cancel` | explicit start, stop, discard |
| `WhisperLoom pause` · `resume` | pause and continue the same recording |
| `WhisperLoom stage next` · `stage prompt` | cycle or set the stage (`off`, `polish`, `beautify`, `summarize`, `prompt`) |
| `WhisperLoom status --json` | current state as JSON (keys only as present/missing) |
| `WhisperLoom show` | open the stage view |

The result goes to the clipboard — paste it with `Ctrl+V`.

What happens on `toggle` → `toggle`: recording stops, the audio goes (in memory) to the recognition provider with
`language=en`, the transcript goes to Claude with the English **Prompt** instructions, and the result lands in the
clipboard and in the stage view. If Claude fails, you still get the transcript (with the fixed rules applied) — a
dictation is never lost.

## 4. Stage view on the projector

Tray → **Stage view** (or `WhisperLoom show`):

- big recording indicator (pulsing red) with timer and level meter
- left: raw text, right: result — default font size 32
- `Ctrl` + `+` / `-` zoom, `Ctrl` + `0` reset, `F11` full screen
- **Copy** buttons above each side; stage and language at the top right (click the stage to cycle)

## 5. Privacy

Audio stays in memory and is never written to disk. The history keeps text only (raw, result, stage, provider,
time; on/off and size in **Privacy**). Logs (`~/.config/whisperloom/logs/`) contain metadata only: duration,
status, stage, error class. Provider and model labels that contain spaces are left out on purpose (logged as
`(verworfen)`): the log never takes free text.

## Troubleshooting

- **“Recognition not set up”:** recognition provider or its key missing — see step 2.1.
- **“Recognition: server reported an error”:** usually a wrong recognition key; **Check access** tells.
- **Result equals the raw text:** the text access failed (wrong Claude key, no credit) — **Check access** on the
  **Text** tab tells; `WhisperLoom status --json` then shows `"lastModel": null`.

- **“No usable microphone”:** check `alsa-lib`/`pipewire-alsa` and pick the microphone under **Output**.
- **Nothing in the clipboard:** install `wl-clipboard` (Wayland) or `xclip` (X11).
- **`WhisperLoom toggle` does nothing:** start the app once first; `WhisperLoom status --json` shows whether it runs.
