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

## 7. Company server / Firmen-Manager (`server/`, Python)

The self-hostable company server (`server/`, also shipped as a Docker image based on `python:3.12-slim`) uses the
Python packages below, pinned in `server/requirements.txt`. Licenses were taken from the installed package metadata
(`License-Expression` or classifiers) on 2026-11-01.

Der selbst hostbare Firmen-Manager (`server/`, auch als Docker-Image auf Basis `python:3.12-slim`) nutzt die folgenden
Python-Pakete, fest in `server/requirements.txt`. Lizenzen laut installierten Paket-Metadaten, Stand 2026-11-01.

### Runtime / Laufzeit

| Package | Version | License | Upstream |
|---|---|---|---|
| annotated-doc | 0.0.5 | MIT | https://github.com/fastapi/annotated-doc |
| annotated-types | 0.8.0 | MIT | https://github.com/annotated-types/annotated-types |
| anyio | 4.15.1 | MIT | https://github.com/agronholm/anyio |
| argon2-cffi | 25.1.0 | MIT | https://github.com/hynek/argon2-cffi |
| argon2-cffi-bindings | 26.1.0 | MIT | https://github.com/hynek/argon2-cffi-bindings |
| certifi | 2026.10.23 | MPL-2.0 | https://github.com/certifi/python-certifi |
| cffi | 2.1.1 | MIT-0 | https://github.com/python-cffi/cffi |
| click | 8.5.0 | BSD-3-Clause | https://github.com/pallets/click |
| colorama (Windows only / nur Windows) | 0.4.6 | BSD-3-Clause | https://github.com/tartley/colorama |
| cryptography | 50.0.2 | Apache-2.0 OR BSD-3-Clause | https://github.com/pyca/cryptography |
| fastapi | 0.162.0 | MIT | https://github.com/fastapi/fastapi |
| h11 | 0.16.0 | MIT | https://github.com/python-hyper/h11 |
| httpcore | 1.0.9 | BSD-3-Clause | https://github.com/encode/httpcore |
| httpx | 0.28.1 | BSD-3-Clause | https://github.com/encode/httpx |
| idna | 3.20 | BSD-3-Clause | https://github.com/kjd/idna |
| Jinja2 | 3.1.6 | BSD-3-Clause | https://github.com/pallets/jinja |
| MarkupSafe | 3.0.4 | BSD-3-Clause | https://github.com/pallets/markupsafe |
| opentelemetry-api | 1.45.2 | Apache-2.0 | https://github.com/open-telemetry/opentelemetry-python |
| pycparser | 3.12 | BSD-3-Clause | https://github.com/eliben/pycparser |
| pydantic | 2.14.2 | MIT | https://github.com/pydantic/pydantic |
| pydantic_core | 2.50.2 | MIT | https://github.com/pydantic/pydantic-core |
| python-multipart | 0.0.32 | Apache-2.0 | https://github.com/Kludex/python-multipart |
| PyYAML | 6.0.3 | MIT | https://github.com/yaml/pyyaml |
| starlette | 1.10.0 | BSD-3-Clause | https://github.com/Kludex/starlette |
| typing_extensions | 4.16.0 | PSF-2.0 | https://github.com/python/typing_extensions |
| typing-inspection | 0.4.4 | MIT | https://github.com/pydantic/typing-inspection |
| uvicorn | 0.54.0 | BSD-3-Clause | https://github.com/Kludex/uvicorn |

### Tests and lint only / Nur Tests und Lint (`server/requirements-dev.txt`, not shipped)

| Package | Version | License |
|---|---|---|
| iniconfig | 2.3.1 | MIT |
| packaging | 26.3 | Apache-2.0 OR BSD-2-Clause |
| pluggy | 1.7.0 | MIT |
| Pygments | 2.21.0 | BSD-2-Clause |
| pytest | 9.2.0 | MIT |
| ruff | 0.17.3 | MIT |

TOTP (RFC 6238) is implemented with the Python standard library; no third-party OTP package is used. The base image
`python:3.12-slim` contains CPython (PSF-2.0) and Debian packages under their respective licenses.
