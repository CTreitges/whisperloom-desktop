# Lokale Modelle und eigene Anbieter / Local models and custom providers

[Deutsch](#deutsch) · [English](#english)

## Deutsch

WhisperLoom kann ganz ohne Cloud arbeiten. Standard ist Whisper zum Herunterladen; daneben lassen sich eigene
Modelldateien, lokale Server und ein mitgeliefertes Textmodell nutzen. Alles steht in den Einstellungen unter
**Quellen → Lokale Modelle** bzw. **Quellen → Anbieter**; gewählt wird auf den Seiten **Erkennung** und **Text & Stufen**.

### Übersicht

| Weg | Wofür | Einrichten | Auswahl |
|---|---|---|---|
| Whisper (Standard) | Erkennung | Lokale Modelle → Whisper → „Laden“ (Tiny/Base/Small/Large v3 Turbo) | Erkennung → Lokal → Modell |
| Eigene Whisper-Datei | Erkennung | Lokale Modelle → „Datei hinzufügen …“ (`.bin`/`.gguf`, ggml- oder GGUF-Format) | Erkennung → Lokal → Modell „… (eigene Datei)“ |
| Lokaler Server | Erkennung oder Text | Lokale Modelle → Lokale Server → „Server hinzufügen“ | als Anbieter „Name · lokaler Server“ in jedem Zugang; für Text auch unter Text & Stufen → Lokales Textmodell „Lokaler Server“ |
| Mitgeliefertes Textmodell | Text | Lokale Modelle → Textmodell → „Laden“ | Text & Stufen → Lokales Textmodell „Mitgeliefertes Modell“ |
| Ollama | Text | Ollama selbst installieren, Modell mit `ollama pull` | Text & Stufen → Lokales Textmodell „Ollama“ |
| Eigener Anbieter | Erkennung und/oder Text | Anbieter → „Anbieter hinzufügen“ | als Anbieter „Name · eigener Anbieter“ in jedem Zugang |

### Eigene Whisper-Datei

- Erlaubt sind ggml-Dateien (wie die von whisper.cpp, Magic `lmgg`) und GGUF-Dateien (Magic `GGUF`), z. B. ein
  feinjustiertes deutsches Modell. Die Datei bleibt, wo sie ist; WhisperLoom merkt sich nur Name und Pfad
  (`offline.customModels`, gerätebezogen: Export und Import lassen sie weg).
- Der Begleitprozess startet dann mit `-m <pfad>`. Fehlt die Datei oder ist sie keine Modelldatei, meldet das Diktat
  das klar („Eigene Whisper-Datei … nicht gefunden“). Es gibt keinen stillen Rückfall auf Small.

### Lokale Server (OpenAI-kompatibel)

Ein Eintrag hat Name, Art (**Erkennung** = `/audio/transcriptions`, **Text** = `/chat/completions`), Adresse (endet
meist auf `/v1`) und optional ein Modell (leer = erstes Modell des Servers). „Zugang prüfen“ ruft `/models` ab. Lokale
Server bekommen **nie** einen Key. `http://` ist nur zu Adressen im eigenen Netz erlaubt (localhost, LAN, VPN,
Tailscale).

| Server | Art | Beispiel-Adresse | Hinweis |
|---|---|---|---|
| LM Studio | Text | `http://127.0.0.1:1234/v1` | im Tab „Developer“ den Server starten |
| llama.cpp `llama-server` | Text | `http://127.0.0.1:8080/v1` | `llama-server -m modell.gguf --port 8080` |
| faster-whisper-server / speaches | Erkennung | `http://127.0.0.1:8000/v1` | Modell z. B. `Systran/faster-whisper-large-v3` |
| Ollama (OpenAI-Schicht) | Text | `http://127.0.0.1:11434/v1` | alternativ den Weg „Ollama“ nutzen |

Als Anbieter sind lokale Server auch in der Ersatzkette wählbar (Ersatz-Erkennung, Ersatz-Textmodell).

### Mitgeliefertes Textmodell (llama.cpp)

- WhisperLoom lädt beim ersten Bedarf `llama-server` aus dem **offiziellen llama.cpp-Release** `b11569` (Windows x64
  CPU und Vulkan, Linux x64 CPU und Vulkan; Größe und SHA-256 im Code festgeschrieben) und das gewählte Modell von
  Hugging Face (fester Commit, SHA-256, fortsetzbar, Platzprüfung, drei Wiederholungen).
- Der Begleitprozess startet bei Bedarf, schon **während der Aufnahme** (wenn danach lokal verbessert wird), lauscht nur
  auf 127.0.0.1, endet nach Leerlauf (wie Whisper, Standard 10 min) und mit der App. Erst Vulkan, bei Fehler CPU.
- Die Prompts sind dieselben wie online; nur der Transport ist lokal.

| Modell | Datei | RAM-Minimum | Lizenz |
|---|---|---|---|
| Qwen2.5 1.5B Instruct (Empfehlung) | `qwen2.5-1.5b-instruct-q4_k_m.gguf`, 1,1 GB | 8 GB | Apache-2.0 |
| Qwen3 4B Instruct 2507 | `Qwen3-4B-Instruct-2507-Q4_K_M.gguf`, 2,5 GB (Quantisierung: Unsloth) | 16 GB | Apache-2.0 |

**Lizenzprüfung:** Angeboten werden nur Modelle mit freier Lizenz (Apache-2.0/MIT), die der User selbst von Hugging Face
lädt. Nicht angeboten: **Gemma** (Gemma Terms of Use: Weitergabe nur mit den Nutzungsbeschränkungen; die Repositories
von Google sind „gated“, ein Download ohne Anmeldung und Zustimmung bei Hugging Face ist nicht möglich) und
**Qwen2.5-3B** (Qwen Research License, nicht kommerziell).

### Ollama

Unverändert: Ollama selbst installieren (https://ollama.com/download), Modell per `ollama pull` holen, unter
Text & Stufen „Ollama“ wählen. Adresse unter „Erweitert“ (Standard `http://127.0.0.1:11434`).

### Eigene Anbieter

Ein eigener Anbieter (`providers.custom`) hat Name, Protokoll (OpenAI-kompatibel: Erkennung und Text; Ollama und
Anthropic: Text; ElevenLabs: Erkennung), Adresse, optional Modell-IDs (leer = „Modelle vom Server laden“) und „Ohne
Key erlaubt“. Sein **Key gehört zum Eintrag** und geht nur an dessen Adresse; ein Zugang mit diesem Anbieter nutzt nie
den Key eines Cloud-Anbieters. Ein Import, der die Adresse auf einen anderen Host ändert, löscht den gespeicherten Key
(Warnung in der Vorschau).

### Modelle vom Server

Bei jeder Modellwahl lädt „Modelle vom Server laden“ die Liste des Anbieters (OpenAI-Form `/models`, Ollama
`/api/tags`). Die Auswahl zeigt dann Katalog, Serverliste („· Server“) und „Eigenes Modell …“. Fehler stehen unter
der Auswahl; die zuletzt geladene Liste bleibt.

## English

WhisperLoom can work without any cloud. Downloadable Whisper models are the default; you can also use your own model
files, local servers and a bundled text model. Set them up under **Sources → Local models** and **Sources →
Providers**; choose them on the pages **Recognition** and **Text & stages**.

### Overview

| Option | Used for | Set up | Choose |
|---|---|---|---|
| Whisper (default) | recognition | Local models → Whisper → “Download” | Recognition → Local → Model |
| Own Whisper file | recognition | Local models → “Add file …” (`.bin`/`.gguf`, ggml or GGUF) | Recognition → Local → “… (own file)” |
| Local server | recognition or text | Local models → Local servers → “Add server” | provider “Name · local server” in any access; for text also Text & stages → Local text model “Local server” |
| Bundled text model | text | Local models → Text model → “Download” | Text & stages → Local text model “Bundled model” |
| Ollama | text | install Ollama yourself, `ollama pull` a model | Text & stages → Local text model “Ollama” |
| Custom provider | recognition and/or text | Providers → “Add provider” | provider “Name · custom provider” in any access |

### Own Whisper file

ggml files (magic `lmgg`, as used by whisper.cpp) and GGUF files (magic `GGUF`) are accepted. The file stays where it
is; only its name and path are stored (`offline.customModels`, device-bound: never exported or imported). The sidecar
starts with `-m <path>`. A missing or broken file gives a clear error — there is no silent fallback to Small.

### Local servers (OpenAI-compatible)

An entry has a name, a kind (**Recognition** = `/audio/transcriptions`, **Text** = `/chat/completions`), an address
(usually ending in `/v1`) and an optional model (empty = the server's first model). “Check access” calls `/models`.
Local servers **never** receive a key. `http://` is only allowed to addresses in your own network.

| Server | Kind | Example address |
|---|---|---|
| LM Studio | text | `http://127.0.0.1:1234/v1` |
| llama.cpp `llama-server` | text | `http://127.0.0.1:8080/v1` |
| faster-whisper-server / speaches | recognition | `http://127.0.0.1:8000/v1` |
| Ollama (OpenAI layer) | text | `http://127.0.0.1:11434/v1` |

Local servers can also be chosen in the fallback chain.

### Bundled text model (llama.cpp)

WhisperLoom downloads `llama-server` from the **official llama.cpp release** `b11569` (Windows x64 CPU/Vulkan, Linux
x64 CPU/Vulkan; size and SHA-256 pinned) and the chosen model from Hugging Face (pinned commit, SHA-256, resumable,
disk-space check, three retries). The sidecar starts on demand — already **while you record** if local refinement
follows —, listens on 127.0.0.1 only, stops after idle time and with the app. Vulkan first, CPU on failure. Prompts are
the same as online; only the transport is local.

| Model | Minimum RAM | Licence |
|---|---|---|
| Qwen2.5 1.5B Instruct (recommended, 1.1 GB) | 8 GB | Apache-2.0 |
| Qwen3 4B Instruct 2507 (2.5 GB, quantized by Unsloth) | 16 GB | Apache-2.0 |

**Licence check:** only freely licensed models (Apache-2.0/MIT) that the user downloads from Hugging Face are offered.
Not offered: **Gemma** (Gemma Terms of Use require passing on use restrictions; Google's repositories are gated, so no
anonymous download) and **Qwen2.5-3B** (Qwen Research License, non-commercial).

### Ollama

Unchanged: install Ollama (https://ollama.com/download), `ollama pull` a model, choose “Ollama” under Text & stages.

### Custom providers

A custom provider (`providers.custom`) has a name, a protocol (OpenAI-compatible: recognition and text; Ollama and
Anthropic: text; ElevenLabs: recognition), an address, optional model IDs and “Key optional”. Its **key belongs to the
entry** and is only sent to its address; an access using it never sends a cloud provider's key there. An import that
moves the address to another host deletes the stored key (warning in the preview).

### Models from the server

“Load models from server” fetches the provider's list (`/models`, Ollama `/api/tags`). The picker then shows catalog,
server list (“· server”) and “Custom model …”. Errors appear below the picker; the last list is kept.
