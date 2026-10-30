# whisper-server-Build (Begleitprozess für die lokale Erkennung)

## Zweck
WhisperLoom erkennt Sprache lokal über `whisper-server` aus [whisper.cpp](https://github.com/ggml-org/whisper.cpp), gestartet als Begleitprozess auf 127.0.0.1. whisper.cpp liefert für Windows x64 offiziell nur CPU- und CUDA-Builds. CUDA 12/13 unterstützt Pascal-GPUs (z. B. GTX 1080 Ti) nicht mehr bzw. ist mehrere hundert MB groß. Deshalb bauen wir selbst:

| Variante | Backend | Läuft auf |
|---|---|---|
| `vulkan` | ggml-vulkan | NVIDIA, AMD, Intel mit Vulkan-1.2-Treiber |
| `cpu` | ggml-cpu (AVX2-Baseline) | jede x64-CPU mit AVX2 (Rückfall) |

Ziele: `win-x64` (windows-latest) und `linux-x64` (ubuntu-24.04, bewusst nicht `ubuntu-latest` = 26.04 mit glibc 2.43), also vier Archive.

## Workflow
`.github/workflows/whisper-server.yml`:
- Auslöser: `workflow_dispatch` (Eingaben `whisper_tag`, `whisper_commit`) und Push auf `ci/whisper-server`.
- Klont whisper.cpp am festen Tag (kein Submodul) und bricht ab, wenn der Commit nicht dem gepinnten entspricht.
- Vulkan SDK von LunarG mit gepinnter Version und SHA256 (`VULKAN_VERSION`, `VULKAN_SHA256_*`). Unter Linux kommt der Loader `libvulkan.so` aus apt (`libvulkan-dev`), weil das SDK-Tarball ihn nicht mehr enthält.
- Actions auf Commit-SHA gepinnt (Versionskommentar dahinter). Neue SHA über den Tag ermitteln: `gh api repos/actions/<name>/git/ref/tags/<vX.Y.Z>` (Typ `commit`; bei `tag` über `.object.url` dereferenzieren).
- CMake: `BUILD_SHARED_LIBS=OFF`, `GGML_NATIVE=OFF`, `GGML_OPENMP=OFF`, `GGML_VULKAN=ON|OFF`; Windows zusätzlich statische MSVC-Laufzeit. Ergebnis ist eine einzelne Datei ohne vcruntime/vcomp/libgomp. Der Vulkan-Build braucht zur Laufzeit nur den Vulkan-Loader des Grafiktreibers (`vulkan-1.dll` bzw. `libvulkan.so.1`).
- Artefakte (Name = Artefakt im Lauf):
  - `win-vulkan`, `win-cpu`: `whisper-server-<tag>-win-x64-<variante>.zip`
  - `linux-vulkan`, `linux-cpu`: `whisper-server-<tag>-linux-x64-<variante>.tar.gz`
  - `sidecar-manifest`: `SHA256SUMS.txt` und `sidecar-manifest.json` (`tag`, `commit`, `vulkanSdk`, je Datei `file`, `sha256`, `variant`, `os`, `arch`)
- Jedes Archiv enthält `whisper-server[.exe]`, `LICENSE-whisper.cpp.txt` (MIT) und `BUILD-INFO.txt`.
- Die Linux-Binaries werden auf Ubuntu 24.04 gebaut und brauchen glibc ≥ 2.39 (Manjaro, Arch, Ubuntu 24.04+, Fedora 40+).

## Tag-Wahl
- Die Hauptversion folgt Android (v1.9.x). Die Binaries von whisper.cpp hängen an Build-Tags `bNNNN`, nicht an `vX.Y.Z`.
- Aktueller Tag: `gh release list -R ggml-org/whisper.cpp --limit 10`. Wir nehmen den `bNNNN`-Tag, der auf denselben Commit zeigt wie die neueste `v1.9.x` und Windows-Assets hat:
  - `gh release view <bNNNN> -R ggml-org/whisper.cpp --json targetCommitish,assets`
  - Stand 2026-10-30: `b5454` = `d1be6fde11ac6e0407606b4e42fe72d34add8037` = v1.9.5.

## Neuer Build
1. Tag und Commit wie oben bestimmen.
2. Optional: neue Vulkan-SDK-Version aus `https://vulkan.lunarg.com/sdk/latest.json`, SHA256 aus `https://sdk.lunarg.com/sdk/sha/<ver>/<windows|linux>/<datei>.txt`.
3. Defaults in `whisper-server.yml` (`inputs.*.default` und `env`) anpassen, auf einem Branch pushen, oder ohne Änderung starten:
   `gh workflow run whisper-server.yml -R CTreitges/whisperloom-desktop -f whisper_tag=<bNNNN> -f whisper_commit=<sha>` (nur vom Default-Branch aus).
4. Prüfen:
   - `gh run download <run-id> -R CTreitges/whisperloom-desktop -D <ordner>`
   - Prüfsummen: jede Zeile aus `SHA256SUMS.txt` gegen die Datei.
   - Funktion: `py -3 -X utf8 tools/sidecar_probe.py --server <whisper-server> --model <ggml-tiny.bin> --audio jfk.wav --expect "fellow Americans" [--expect-device "<GPU>"]`. Modelle und Samples liegen außerhalb des Repos.

## Release (nur nach Christofs OK)
Pre-Release `whisper-server-<tag>` mit den vier Archiven, `SHA256SUMS.txt` und `sidecar-manifest.json`:

```
gh release create whisper-server-<tag> -R CTreitges/whisperloom-desktop --prerelease \
  --target <commit-des-workflows> --title "whisper-server <tag>" \
  --notes "whisper.cpp <tag> (<commit>), Vulkan SDK <ver>. Build: run <run-id>." \
  <ordner>/win-vulkan/*.zip <ordner>/win-cpu/*.zip \
  <ordner>/linux-vulkan/*.tar.gz <ordner>/linux-cpu/*.tar.gz \
  <ordner>/sidecar-manifest/SHA256SUMS.txt <ordner>/sidecar-manifest/sidecar-manifest.json
```

Die App lädt die Binaries später von diesem Release und prüft sie gegen die SHA256 aus `sidecar-manifest.json`.
