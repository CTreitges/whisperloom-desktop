# Changelog

All notable changes to WhisperLoom Desktop. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- **Setup wizard** on first start: language, microphone with level test, recognition (cloud with key and
  *Check access*, or local with model download), stage, shortcut test (Windows) or desktop-shortcut guide (Linux),
  autostart, done. Skippable; run it again from the tray (*Setup …*) or with `WhisperLoom setup`. Steps locked by an
  IT policy are left out. Existing installations see the wizard once after updating (skip is enough).
- **Autostart switch**: Windows `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, Linux
  `~/.config/autostart/whisperloom.desktop`; current user only, no admin rights.
- CLI verb `setup`.
- Docs: bilingual `README.md`, `docs/proxy.md` (system proxy, PAC, manual, NTLM/Kerberos, `HTTPS_PROXY` on Linux),
  `docs/linux-shortcuts.md` (KDE, GNOME, Wayland/X11), `THIRD_PARTY.md`, this changelog.
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
