# Changelog

All notable changes to WhisperLoom Desktop. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Company proxy: vocabulary (`keywords[]`) and “German + English” (two `languages[]`) now reach the upstream provider
  like with direct access (OpenAI gpt-transcribe, Mistral `context_bias[]`, ElevenLabs `keyterms`); strict field checks
  in the proxy. An own key stored (CLI `key set` or key field) while the company fixes the target is no longer sent to
  that target after leaving the company. Server README: lockout behind the same IP, precise note on forged
  `X-Forwarded-For` (W3-REST).
- Company server upgrade note (W3-REST): update the company server before the apps, otherwise without vocabulary /
  multiple languages. Against a 1.0.0 server the app retries a rejected recognition (`400 invalid_request`) once
  without `keywords[]` and with automatic language detection instead of “German + English”.
- **Next dictation while the text is being improved** (W3-NEXT): pressing the dictation hotkey (hold or toggle, Linux
  `toggle`) during the AI improvement now starts the next recording right away; the running dictation is still improved
  and inserted into its own window, results arrive in dictation order. Skip the improvement with a double-press (< 400 ms)
  or Esc (Windows). Deliberate deviation from Android (`docs/parity-exceptions.md`).
- **Update channels** (W3-CHAN): Stable, Beta and Test (Velopack channels `win`/`linux`, `*-beta`, `*-test`), setting
  „Update channel“ and „Install pre-releases automatically“ (default off: automatic updates stay stable), „Choose
  version …“ in the tray and on the General page to install any version, including a confirmed downgrade back to
  stable. Test builds only on manual trigger (`X.Y.Z-test.<run>.<branch>`), betas from tags `vX.Y.Z-beta.N`; the
  update source now reads up to 100 GitHub releases (Velopack's default only saw the latest 10).
- **`--help` / `-h`** (W3-PERF): print the command overview (exit 0) without starting the app; `help` still opens the
  help window. `build/pack.py` now runs `WhisperLoom --version` on the published binary before packing and stops if it fails.

### Fixed
- **Settings window** (W3-PERF): Tab right after ↑/↓ in the sidebar no longer leaves nothing focused (focus returns to
  the sidebar); pre-built pages are no longer exposed to screen readers (UIA/AT-SPI) and never inherit the window's
  data for a moment; the Providers page reads its keys once instead of on every visit.
- **Command line** (W3-FUND): redirected output (scripts, pipes) is now UTF-8 on Windows too; umlauts in `warn`/`error`
  lines no longer arrive as replacement characters.

## [1.0.0] – 2026-11-02

### Added
- **Company manager** (`server/`, self-hosted, Docker or systemd): multi-tenant web portal with device approval by
  company code, groups, a three-level policy per setting (Fixed / Default / Free), provider keys (distributed or proxy
  only), usage statistics and budget limits (`403 budget_exhausted`, no silent fallback), admins with optional
  two-factor sign-in (can be skipped and turned on later; an owner can require it), trusted-header sign-in, file mode,
  SSRF protection with port-exact allow list. Client API: `docs/server-api.md`.
- **Company connection in the app**: join by company code, link or provisioning file, config sync, the three policy
  levels in the settings UI, proxy access through the server; leaving drops all own keys pointed at the company.
- **New settings UI** after the Apple HIG design system, built-in **help** with illustrations and screenshots (F1, „?“).
- **Own providers and local models**: own OpenAI-compatible servers, local servers (LM Studio, llama.cpp,
  faster-whisper-server), own Whisper files, bundled llama.cpp text model.
- **Process the selection**: stage picker at the cursor, hotkey, button, Ctrl+right-click, tray, Linux verb.
- **Output and history**: reliable paste with clipboard restore in browsers/Electron, stage picker, Enter after insert
  as a setting.
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

### Changed
- **Retiring OpenAI models removed** (Android TD-A4): gpt-5-mini, gpt-5-nano (shutdown 2026-12-11), gpt-4o-transcribe,
  gpt-4o-mini-transcribe and whisper-1 (shutdown 2027-02-26) are no longer offered; new: GPT-5.6 Terra. Saved, imported,
  provisioned and company-set selections move to the successor when loaded (gpt-5.6-terra, gpt-5.6-luna, gpt-transcribe);
  other providers and own servers are left alone. A fixed company value is moved too and logged (path only).
- „German + English“ (`de+en`) is a new value of `language`: an older WhisperLoom rejects such a settings file on
  import and, if downgraded, would send `de+en` to the provider. Pick another language before downgrading.

### Note for testers of the 1.0.0 pre-release
The earlier 1.0.0 pre-release has the same version number and is **not** updated automatically. Uninstall it (or
delete the portable folder) and install this release once; settings stay in place.

### Performance
Measured with `desktop-perf-review` on one PC (Windows 11, 179 Hz), report: `docs/perf/2026-11-02-perf-review.md`.
- **Faster start**: release packages are compiled ReadyToRun (Linux additionally as composite; Windows without composite
  so the .NET runtime keeps Microsoft's signatures). Ready after start p50 ≈ 1.57 s → ≈ 1.0 s (979–1002 ms over
  several runs), window visible 1205 → 652 ms. Downloads grow by ≈ 8 MB (Windows) and ≈ 18 MB (Linux).
  ReadyToRun also re-writes `SkiaSharp.dll`, `HarfBuzzSharp.dll` and `CommunityToolkit.Mvvm.dll`, which drops their
  vendor signatures (see `docs/signing.md`).
- **Snappier settings**: pages are built once and reused, prepared in small steps right after the window opens, and the
  sidebar highlight shows before the page is attached. Clicking „Recognition“ the first time (page already prepared,
  ≥ 3 s after opening): first visible reaction 266 → 34 ms, longest UI-thread block 141 → 55 ms; the preparation itself
  blocks for up to ≈ 77 ms right after the window opens. Scroll position and opened „Advanced“ sections now stay when you
  switch pages.
- **Help illustrations** resolve brushes and pens once per theme instead of on every render (11 KB → 0.7 KB allocated
  per render).
- New verbs `settings` and `help` open the settings and the help window (also used for the measurements).

## [0.1.0-demo.1] – 2026-10-31 (demo pre-release)

### Added
- Core port of WhisperLoom for Android (3.9.5): providers, stages including Prompt, filler words, paragraphs, context
  words, fallback chain; prompt and provider parity checks against the Android sources.
- Avalonia app for Windows and Linux: tray, single instance with IPC and CLI (`toggle`, `start`, `stop`, `cancel`,
  `pause`, `resume`, `stage`, `status`, `show`, `quit`, `key`), stage view with zoom, settings in German and English,
  masked keys in the OS keyring, proxy, history, metadata-only logs, audio kept in memory only.
- CI build of `whisper-server` (whisper.cpp `b5454`) for win-x64 and linux-x64, Vulkan and CPU.

[Unreleased]: https://github.com/CTreitges/whisperloom-desktop/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/CTreitges/whisperloom-desktop/compare/v0.1.0-demo.1...v1.0.0
[0.1.0-demo.1]: https://github.com/CTreitges/whisperloom-desktop/releases/tag/v0.1.0-demo.1
