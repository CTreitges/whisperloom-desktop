# WhisperLoom Desktop — Demo guide (Manjaro / Linux, also Windows)

This guide gets WhisperLoom running for a live demo: dictate in English, let the **Prompt** stage turn the
dictation into a clean prompt, and show raw text and result side by side on a projector.

> Status (demo build): the app shell is complete — tray, stage view, settings, key storage, CLI. The online
> recognition and text pipeline is wired in by the integration step (WLD-INT); until then the app answers with a
> built-in fake pipeline (`[prompt] Das ist ein Testdiktat.`).

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

## 2. Enter the key

Tray → **Settings** → **Text** tab:

- **Stage:** `Prompt`, and tick **Offer the “Prompt” stage**.
- **Access for text refinement:** `Anthropic (Claude)` → **Replace** → paste the Claude key → **Save**.
  Keys are always masked; there is no way to reveal them. **Check access** tests the key against the provider.

**Recognition** tab: provider (e.g. OpenAI or Groq), its key via **Replace**, **Language** `EN`.
Then press **Save** at the bottom.

Keys are stored in the OS keyring (Windows Credential Manager, Linux Secret Service) — never in `settings.json`.

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

## 4. Stage view on the projector

Tray → **Stage view** (or `WhisperLoom show`):

- big recording indicator (pulsing red) with timer and level meter
- left: raw text, right: result — default font size 32
- `Ctrl` + `+` / `-` zoom, `Ctrl` + `0` reset, `F11` full screen
- **Copy** buttons above each side; stage and language at the top right (click the stage to cycle)

## 5. Privacy

Audio stays in memory and is never written to disk. The history keeps text only (raw, result, stage, provider,
time; on/off and size in **Privacy**). Logs (`~/.config/whisperloom/logs/`) contain metadata only: duration,
provider, status, error class.

## Troubleshooting

- **“No usable microphone”:** check `alsa-lib`/`pipewire-alsa` and pick the microphone under **Output**.
- **Nothing in the clipboard:** install `wl-clipboard` (Wayland) or `xclip` (X11).
- **`WhisperLoom toggle` does nothing:** start the app once first; `WhisperLoom status --json` shows whether it runs.
