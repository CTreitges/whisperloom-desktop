"""Suite "offline" von tools/smoke.py: lokale Erkennung und Textverbesserung ohne Netz-Fakes (Goal-File A4.4).

Vorbereitung (einmal je Arbeitsordner, braucht Netz): `bench --model tiny --seconds 1 --backend cpu` lädt Modell und
CPU-Begleitprozess nach <work>/<ID>/offline-cache/local; jfk.wav (whisper.cpp-Sample, gepinnter Commit, SHA256 geprüft)
kommt in denselben Ordner. Danach läuft jede Prüfung mit eigenem Datenordner, in den der Cache kopiert wird, ohne
tools/fake_api.py, Ausgabe-Senke --output-file, Key-Präfix WhisperLoom-Test-<ID>: (keine Keys nötig).

Prüfungen:
- offline-stt: Engine "offline", Stufe aus: jfk.wav wird lokal erkannt ("fellow Americans"), der Begleitprozess lief
  auf 127.0.0.1 und endet mit der App (gespeicherte PID).
- offline-refine-ollama: Stufe Glätten, Regel "lokal", Ollama-Fake (eigener Port über local.ollamaUrl): Ergebnis vom
  Fake, Modell = Fake-Modell. Ein echtes Ollama wird nur gelesen (/api/tags) und nur gemeldet.
- raw-plus-rules: wie oben, aber kein Ollama erreichbar: Rohtext + Regeln, kein Textverlust, Hinweis gesetzt.

Prozesse startet und beendet das Skript nur über die eigene PID.
"""
from __future__ import annotations

import hashlib
import json
import shutil
import socket
import subprocess
import sys
import threading
import time
import urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

JFK_URL = "https://raw.githubusercontent.com/ggml-org/whisper.cpp/d1be6fde11ac6e0407606b4e42fe72d34add8037/samples/jfk.wav"
JFK_SHA256 = "59dfb9a4acb36fe2a2affc14bacbee2920ff435cb13cc314a08c13f66ba7860e"
EXPECT = "fellow americans"
FAKE_MODEL = "fake-ollama:1b"
SMALL_MODEL_HINTS = (":0.5b", ":1b", ":1.5b", ":2b", "e2b", ":3b", ":270m", ":360m")


class FakeOllama:
    """Minimaler Ollama-Fake auf 127.0.0.1:<frei>: /api/tags und /api/chat ("[fake-ollama] " + Diktat)."""

    def __enter__(self) -> FakeOllama:
        chats = self.chats = []

        class Handler(BaseHTTPRequestHandler):
            def log_message(self, *args):  # keine Ausgabe (Diktattext)
                pass

            def _send(self, body: dict) -> None:
                data = json.dumps(body).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def do_GET(self):
                self._send({"models": [{"name": FAKE_MODEL, "model": FAKE_MODEL}]})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))) or b"{}")
                chats.append(body.get("model"))
                user = body.get("messages", [{}, {}])[-1].get("content", "")
                text = user.split("\n", 1)[-1].rsplit("\n", 1)[0] if user.startswith("<") else user
                self._send({"message": {"role": "assistant", "content": "[fake-ollama] " + text.strip()},
                            "done": True, "done_reason": "stop"})

        self.server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        self.url = f"http://127.0.0.1:{self.server.server_address[1]}"
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        return self

    def __exit__(self, *exc) -> None:
        self.server.shutdown()
        self.server.server_close()


def free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def pid_alive(platform: str, pid: int) -> bool:
    if platform == "linux":
        try:
            return Path(f"/proc/{pid}/comm").read_text().strip() == "whisper-server"
        except OSError:
            return False
    out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"], capture_output=True, text=True, encoding="utf-8", errors="replace").stdout or ""
    return "whisper-server" in out


def wait_gone(platform: str, pid: int, seconds: float = 15) -> bool:
    deadline = time.time() + seconds
    while pid_alive(platform, pid):
        if time.time() > deadline:
            return False
        time.sleep(0.3)
    return True


def prepare(args, cache: Path) -> Path:
    """Modell, CPU-Begleitprozess (über das Verb bench) und jfk.wav in den Cache; liefert den Pfad von jfk.wav."""
    cache.mkdir(parents=True, exist_ok=True)
    r = subprocess.run([args.app, "bench", "--model", "tiny", "--seconds", "1", "--backend", "cpu", "--data-dir", str(cache)],
                       capture_output=True, text=True, encoding="utf-8", timeout=900)
    if r.returncode != 0:
        raise RuntimeError(f"bench für den Cache: rc={r.returncode} {r.stderr.strip()[-300:]}")
    jfk = cache / "jfk.wav"
    if not jfk.exists() or hashlib.sha256(jfk.read_bytes()).hexdigest() != JFK_SHA256:
        data = urllib.request.urlopen(JFK_URL, timeout=60).read()
        if hashlib.sha256(data).hexdigest() != JFK_SHA256:
            raise RuntimeError("jfk.wav: Prüfsumme passt nicht")
        jfk.write_bytes(data)
    return jfk


def settings(stage: str, ollama_url: str) -> dict:
    return {
        "language": "en",
        "engine": "offline",
        "offline": {"model": "tiny", "accurate": False, "refine": "local"},
        "refine": {"stage": stage},
        "local": {"backend": "cpu", "ollamaModel": FAKE_MODEL, "ollamaUrl": ollama_url},
    }


def run_app(args, smoke, work: Path, cache: Path, jfk: Path, name: str, conf: dict):
    """Eine App-Instanz mit eigenem Datenordner (Cache hineinkopiert); liefert (Status nach dem Diktat, Sidecar-PID, gone)."""
    data = work / args.id / f"smoke-data-offline-{name}"
    smoke.fresh_dir(data)
    shutil.copytree(cache / "local", data / "local")
    (data / "settings.json").write_text(json.dumps(conf), encoding="utf-8")
    out = work / args.id / f"smoke-output-offline-{name}.txt"
    out.unlink(missing_ok=True)
    prefix = f"WhisperLoom-Test-{args.id}:"
    app = smoke.AppRun(args.app, data, prefix, ["--test-audio", str(jfk), "--output-file", str(out)])
    pid = None
    try:
        app.start(timeout=60)
        app.cli("toggle")
        time.sleep(0.3)
        app.cli("toggle", timeout=300)
        s = app.status() or {}
        pid = (s.get("local") or {}).get("sidecarPid")
        s["_out"] = out.read_text(encoding="utf-8") if out.exists() else ""
    finally:
        app.stop()
    gone = pid is None or wait_gone(args.platform, pid)
    return s, pid, gone


def real_ollama_note() -> str:
    """Echtes Ollama nur lesend: gibt es ein kleines Modell? (keine Installation, kein pull)."""
    try:
        with urllib.request.build_opener(urllib.request.ProxyHandler({})).open("http://127.0.0.1:11434/api/tags", timeout=2) as r:
            names = [m.get("name", "") for m in json.loads(r.read()).get("models", [])]
    except (OSError, ValueError):
        return "echtes Ollama: nicht erreichbar"
    small = [n for n in names if any(h in n.lower() for h in SMALL_MODEL_HINTS)]
    return f"echtes Ollama: {len(names)} Modelle, klein: {', '.join(small) or 'keins'} (nur gelesen)"


def run(args, work: Path, report, smoke) -> None:
    cache = work / args.id / "offline-cache"
    jfk = prepare(args, cache)

    s, pid, gone = run_app(args, smoke, work, cache, jfk, "stt", settings("off", f"http://127.0.0.1:{free_port()}"))
    raw = str(s.get("lastRaw") or "")
    local = s.get("local") or {}
    report.check("offline-stt", EXPECT in raw.lower() and s["_out"] != "" and pid is not None and gone
                 and local.get("backend") == "cpu" and s.get("lastError") is None,
                 f"raw={len(raw)} Zeichen enthält={EXPECT in raw.lower()} pid={pid} gone={gone} local={local} error={s.get('lastError')}")

    with FakeOllama() as fake:
        s, pid, gone = run_app(args, smoke, work, cache, jfk, "ollama", settings("polish", fake.url))
        result = str(s.get("lastResult") or "")
        report.check("offline-refine-ollama", result.lower().startswith("[fake-ollama]") and EXPECT in result.lower()
                     and s.get("lastModel") == FAKE_MODEL and fake.chats == [FAKE_MODEL] and gone,
                     f"result={result[:30]!r} model={s.get('lastModel')} chats={fake.chats} gone={gone}")
    print(f"SMOKE INFO {real_ollama_note()}")

    s, pid, gone = run_app(args, smoke, work, cache, jfk, "raw", settings("polish", f"http://127.0.0.1:{free_port()}"))
    result = str(s.get("lastResult") or "")
    raw = str(s.get("lastRaw") or "")
    report.check("raw-plus-rules", EXPECT in raw.lower() and EXPECT in result.lower() and s.get("lastModel") is None
                 and s["_out"] == result and s.get("lastError") is None and gone,
                 f"raw={len(raw)} result={len(result)} model={s.get('lastModel')} error={s.get('lastError')} gone={gone}")


if __name__ == "__main__":
    sys.exit("Aufruf über tools/smoke.py --suite offline")
