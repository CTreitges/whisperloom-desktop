# Third-party software / Software von Dritten

WhisperLoom Desktop is MIT-licensed (`LICENSE`). It ships with or downloads the components below. Licenses were taken
from the NuGet package metadata (`.nuspec`) and the upstream repositories on 2026-11-01. The NuGet list is the output of
`dotnet list WhisperLoom.slnx package --include-transitive`; regenerate this file's tables when packages change.

WhisperLoom Desktop steht unter MIT (`LICENSE`). Die folgenden Komponenten werden mitgeliefert bzw. zur Laufzeit
geladen. Lizenzen laut NuGet-Metadaten (`.nuspec`) und Upstream-Repositories, Stand 2026-11-01.

## 1. Shipped with the app / In der App enthalten (NuGet)

| Package | Version | License | Upstream |
|---|---|---|---|
| Avalonia | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Desktop | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.FreeDesktop | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.FreeDesktop.AtSpi | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.HarfBuzz | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Native | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Remote.Protocol | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Skia | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Themes.Fluent | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Win32 | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.X11 | 12.1.4 | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | BSD-3-Clause (ANGLE, © The ANGLE Project Authors) | https://chromium.googlesource.com/angle/angle |
| Avalonia.BuildServices | 11.3.2 | MIT (build time only / nur beim Bauen) | https://avaloniaui.net/ |
| CommunityToolkit.Mvvm | 8.4.0 | MIT | https://github.com/CommunityToolkit/dotnet |
| HarfBuzzSharp | 8.3.1.3 | MIT (wraps HarfBuzz, “Old MIT” license) | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp.NativeAssets.Linux | 8.3.1.3 | MIT / HarfBuzz Old MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp.NativeAssets.macOS | 8.3.1.3 | MIT / HarfBuzz Old MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp.NativeAssets.WebAssembly | 8.3.1.3 | MIT / HarfBuzz Old MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp.NativeAssets.Win32 | 8.3.1.3 | MIT / HarfBuzz Old MIT | https://github.com/mono/SkiaSharp |
| MicroCom.Runtime | 0.11.6 | MIT | https://github.com/kekekeks/MicroCom |
| PortAudioSharp2 | 1.0.6 | Apache-2.0 (repository license; the package has no license field) | https://github.com/csukuangfj/PortAudioSharp2 |
| org.k2fsa.portaudio.runtime.linux-aarch64 | 1.0.6 | Apache-2.0; contains PortAudio (MIT-style PortAudio license) | https://github.com/csukuangfj/PortAudioSharp2 |
| org.k2fsa.portaudio.runtime.linux-x64 | 1.0.6 | Apache-2.0; contains PortAudio (MIT-style) | https://github.com/csukuangfj/PortAudioSharp2 |
| org.k2fsa.portaudio.runtime.osx-arm64 | 1.0.6 | Apache-2.0; contains PortAudio (MIT-style) | https://github.com/csukuangfj/PortAudioSharp2 |
| org.k2fsa.portaudio.runtime.osx-x64 | 1.0.6 | Apache-2.0; contains PortAudio (MIT-style) | https://github.com/csukuangfj/PortAudioSharp2 |
| org.k2fsa.portaudio.runtime.win-x64 | 1.0.6 | Apache-2.0; contains PortAudio (MIT-style) | https://github.com/csukuangfj/PortAudioSharp2 |
| SkiaSharp | 3.119.4 | MIT (wraps Skia, BSD-3-Clause) | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.Linux | 3.119.4 | MIT / Skia BSD-3-Clause | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.macOS | 3.119.4 | MIT / Skia BSD-3-Clause | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.WebAssembly | 3.119.4 | MIT / Skia BSD-3-Clause | https://github.com/mono/SkiaSharp |
| SkiaSharp.NativeAssets.Win32 | 3.119.4 | MIT / Skia BSD-3-Clause | https://github.com/mono/SkiaSharp |
| Tmds.DBus.Protocol | 0.94.1 | MIT | https://github.com/tmds/Tmds.DBus |
| Velopack | 1.2.161 | MIT | https://github.com/velopack/velopack |

The .NET runtime itself (self-contained publish) is MIT-licensed: https://github.com/dotnet/runtime.
Die .NET-Laufzeit (self-contained) steht unter MIT.

**Audio library / Audio-Bibliothek:** recording uses PortAudio (https://github.com/PortAudio/portaudio,
© Ross Bencina and Phil Burk, MIT-style “PortAudio license”) through PortAudioSharp2. Audio stays in memory and is
never written to disk.

## 2. Downloaded at runtime / Zur Laufzeit geladen (only for local recognition / nur bei lokaler Erkennung)

| Component | License | Source |
|---|---|---|
| whisper.cpp `whisper-server` (our CI build, tag `b5454` = v1.9.5, Vulkan or CPU) | MIT, © The ggml authors; each archive contains `LICENSE-whisper.cpp.txt` | https://github.com/ggml-org/whisper.cpp |
| ggml (part of whisper.cpp) | MIT | https://github.com/ggml-org/ggml |
| Whisper models `ggml-*.bin` (tiny, base, small, large-v3-turbo) | MIT (OpenAI Whisper weights, converted by the whisper.cpp project) | https://huggingface.co/ggerganov/whisper.cpp, https://github.com/openai/whisper |

The Vulkan build uses the Vulkan loader of the graphics driver (`vulkan-1.dll` / `libvulkan.so.1`); it is not shipped.
The Vulkan SDK (LunarG) is only used in CI to build `whisper-server`.

**Not shipped / nicht enthalten:** Ollama (MIT, https://github.com/ollama/ollama) is optional and installed by the user;
WhisperLoom only talks to it on 127.0.0.1. Linux helper tools `wl-copy` (wl-clipboard, GPL-3.0), `xclip` (GPL-2.0) and
`secret-tool` (libsecret, LGPL-2.1+) are optional system packages called as separate programs.

## 3. Installer and updates / Installer und Updates

The Windows setup and the Linux AppImage are built with Velopack (`build/pack.py`, `.github/workflows/release.yml`).
Setup und AppImage baut Velopack; die portable ZIP enthält nur das `dotnet publish` der App, ohne Updater.

| Component | Version | License | Source |
|---|---|---|---|
| Velopack runtime (NuGet `Velopack`, see table 1) — update check and install, per-user install hooks | 1.2.161 | MIT | https://github.com/velopack/velopack |
| Velopack `Update.exe`, start stub and `WhisperLoom-win-Setup.exe` bootstrapper (inside the setup, not in the portable ZIP) | 1.2.161 | MIT | https://github.com/velopack/velopack |
| `vpk` CLI (`dotnet-tools.json`, build time only / nur beim Bauen) | 1.2.161 | MIT | https://github.com/velopack/velopack |

Updates come from the GitHub releases of this repository only and can be disabled by IT policy
(`policy.updatesEnabled=false`, see `docs/provisioning.md` and `docs/signing.md`).

## 4. Fonts and assets / Schriften und Grafiken

| Component | License | Source |
|---|---|---|
| Avalonia.Fonts.Inter 12.1.4 (package: MIT) — embeds the Inter typeface | Inter: SIL Open Font License 1.1 | https://github.com/rsms/inter |
| WhisperLoom icon | MIT (this project) | `src/WhisperLoom.App/Assets` |

Avalonia.Fonts.Inter is used by the headless UI tests; the app itself uses the system fonts.
Avalonia.Fonts.Inter nutzen nur die Headless-UI-Tests; die App verwendet die Systemschriften.

## 5. Tests and build only / Nur Tests und Build (not shipped)

| Package | Version | License |
|---|---|---|
| Avalonia.Headless | 12.1.4 | MIT |
| Avalonia.Headless.XUnit | 12.1.4 | MIT |
| Microsoft.ApplicationInsights | 2.23.0 | MIT |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | MIT |
| Microsoft.CodeCoverage | 18.11.0 | MIT |
| Microsoft.NET.Test.Sdk | 18.11.0 | MIT |
| Microsoft.Testing.Extensions.Telemetry | 1.9.1 | MIT |
| Microsoft.Testing.Extensions.TrxReport.Abstractions | 1.9.1 | MIT |
| Microsoft.Testing.Platform | 1.9.1 | MIT |
| Microsoft.Testing.Platform.MSBuild | 1.9.1 | MIT |
| Microsoft.TestPlatform.ObjectModel | 18.11.0 | MIT |
| Microsoft.TestPlatform.TestHost | 18.11.0 | MIT |
| Microsoft.Win32.Registry | 5.0.0 | MIT |
| xunit.analyzers | 1.27.0 | Apache-2.0 |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 |
| xunit.v3 | 3.2.2 | Apache-2.0 |
| xunit.v3.assert | 3.2.2 | Apache-2.0 |
| xunit.v3.common | 3.2.2 | Apache-2.0 |
| xunit.v3.core.mtp-v1 | 3.2.2 | Apache-2.0 |
| xunit.v3.extensibility.core | 3.2.2 | Apache-2.0 |
| xunit.v3.mtp-v1 | 3.2.2 | Apache-2.0 |
| xunit.v3.runner.common | 3.2.2 | Apache-2.0 |
| xunit.v3.runner.inproc.console | 3.2.2 | Apache-2.0 |

## 6. Prompts and parity / Prompts und Parität

The text stages port prompts from WhisperLoom for Android (https://github.com/CTreitges/whisperloom-android, MIT,
same author). The desktop-only prompt “rewrite by instruction” (`src/WhisperLoom.Core/Prompts/desktop/`) is part of this
project (`docs/parity-exceptions.md`). No third-party prompt text is included.
