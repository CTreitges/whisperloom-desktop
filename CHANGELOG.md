# Changelog

All notable changes to WhisperLoom Desktop. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Language „German + English“** (`de+en`, Android CTR-273): GPT Transcribe gets both languages (`languages[]`), all
  other services and the local model detect the language themselves; the detected language counts, otherwise German.
  Hint below the language choice, a separate one for the local model.
- **Vocabulary as word lists**: GPT Transcribe additionally gets `keywords[]` (terms with `<`, `>` or line breaks are
  dropped), Mistral gets `context_bias[]` (up to 100 terms, multi-word terms joined with `_`); free text is no keyword.
- With *Automatic*, the language detected by GPT Transcribe (`languages[0].code`, ISO 639-3 mapped to 639-1) now picks
  filler words and the refinement prompt; before, „auto“ stayed in effect.
- **Setup wizard** on first start: language, microphone with level test, recognition (cloud with key and
  *Check access*, or local with model download), stage, shortcut test (Windows) or desktop-shortcut guide (Linux),
  autostart, done. Skippable; run it again from the tray (*Setup …*) or with `WhisperLoom setup`. Steps locked by an
  IT policy are left out. Existing installations see the wizard once after updating (skip is enough).
- **Autostart switch**: Windows `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Linux
  `~/.config/autostart/whisperloom.desktop`; current user only, no admin rights.
- CLI verb `setup`.
- **Packages and updates** (Velopack): per-user Windows setup without admin rights (`WhisperLoom-win-Setup.exe`,
  installs to `%LocalAppData%\WhisperLoom`), portable ZIP without updater for company distribution, Linux AppImage.
  Update check at start and daily, installed only after confirmation; off with `policy.updatesEnabled=false`.
  `WhisperLoom --version` and `WhisperLoom update [--feed <folder|url>]`.
- Release workflow (tag `v*` → tests → publish → `vpk pack` → GitHub release), unsigned by design;
  `docs/signing.md` explains signing by the company with `signtool`.
- **Windows extras:** last 10 dictations in the tray, hotkey *insert last again* (Ctrl+Alt+Shift+V), *reprocess* from
  the raw text with another stage (no audio kept), **app profiles** (process name, optional window-title regex → stage,
  output mode, Enter afterwards; the stage hotkey changes the active profile), **rewrite selection by voice
  instruction** (Ctrl+Alt+Shift+R; the selection is sent as a delimited data block, new desktop-only prompt DE/EN).
  CLI verbs `reprocess <stage> [<id>]`, `reinsert`, `recent-insert <n>`.
- Docs: bilingual `README.md`, `docs/proxy.md` (system proxy, PAC, manual, NTLM/Kerberos, `HTTPS_PROXY` on Linux),
  `docs/linux-shortcuts.md` (KDE, GNOME, Wayland/X11), `docs/signing.md`, `THIRD_PARTY.md`, this changelog.
- Windows input: hold, toggle and stage hotkeys with recorder and conflict warnings, focus-free overlay and level pill,
  paste with clipboard backup or direct typing.
- Local recognition with a whisper.cpp `whisper-server` sidecar (SHA256-pinned Vulkan and CPU builds), model
  downloads with resume and SHA256 check, Ollama for local text refinement, `bench` verb.
- Settings sync v1 (export/import compatible with Android) and IT provisioning with policy (`provision`, `import`,
  `export`).

## [0.1.0-demo.1] – 2026-10-31 (demo pre-release)

### Added
- Core port of WhisperLoom for Android (3.9.5): providers, stages including Prompt, filler words, paragraphs, context
  words, fallback chain; prompt and provider parity checks against the Android sources.
- Avalonia app for Windows and Linux: tray, single instance with IPC and CLI (`toggle`, `start`, `stop`, `cancel`,
  `pause`, `resume`, `stage`, `status`, `show`, `quit`, `key`), stage view with zoom, settings in German and English,
  masked keys in the OS keyring, proxy, history, metadata-only logs, audio kept in memory only.
- CI build of `whisper-server` (whisper.cpp `b5454`) for win-x64 and linux-x64, Vulkan and CPU.

[Unreleased]: https://github.com/CTreitges/whisperloom-desktop/compare/v0.1.0-demo.1...HEAD
[0.1.0-demo.1]: https://github.com/CTreitges/whisperloom-desktop/releases/tag/v0.1.0-demo.1
