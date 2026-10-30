"""Probe für den Begleitprozess whisper-server: startet ihn auf 127.0.0.1 mit freiem Port, wartet auf
``/health``, schickt eine WAV-Datei an ``/inference`` und prüft den erkannten Text.

Aufruf:
    py -3 -X utf8 tools/sidecar_probe.py --server <whisper-server[.exe]> --model <ggml-*.bin> \
        --audio <datei.wav> --expect "fellow Americans" [--expect-device "GTX 1080 Ti"] [--no-gpu]

Ausgabe: Gerätezeilen aus dem Server-Log (``log: …``), letzte Zeile ``PROBE OK …`` (Exit 0) oder
``PROBE FAIL …`` (Exit 1). Der Server wird nur über die eigene Prozess-Referenz beendet.

Doku: https://github.com/ggml-org/whisper.cpp/blob/b5454/examples/server/README.md (abgerufen 2026-10-30)
"""

from __future__ import annotations

import argparse
import json
import re
import socket
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass
from pathlib import Path

HOST = "127.0.0.1"
# z. B. "ggml_vulkan: 0 = NVIDIA GeForce GTX 1080 Ti (NVIDIA) | uma: 0 | fp16: 0 | …"
VULKAN_DEVICE = re.compile(r"ggml_vulkan: \d+ = (.+?) \(")
DEVICE_LOG = re.compile(r"ggml_vulkan|backend|device", re.IGNORECASE)


@dataclass
class ProbeResult:
    ok: bool
    message: str
    device: str = ""
    text: str = ""


def free_port() -> int:
    with socket.socket() as sock:
        sock.bind((HOST, 0))
        return sock.getsockname()[1]


def _get_status(url: str) -> int:
    try:
        with urllib.request.urlopen(url, timeout=2) as resp:
            return resp.status
    except urllib.error.HTTPError as err:
        return err.code
    except OSError:
        return 0


def _multipart(fields: dict[str, str], file_name: str, file_bytes: bytes) -> tuple[bytes, str]:
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in fields.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    parts.append(
        f'--{boundary}\r\nContent-Disposition: form-data; name="file"; filename="{file_name}"\r\n'
        "Content-Type: audio/wav\r\n\r\n".encode() + file_bytes + b"\r\n"
    )
    parts.append(f"--{boundary}--\r\n".encode())
    return b"".join(parts), f"multipart/form-data; boundary={boundary}"


def transcribe(base_url: str, audio: Path, timeout: float) -> str:
    body, content_type = _multipart({"response_format": "json", "temperature": "0.0"}, audio.name, audio.read_bytes())
    req = urllib.request.Request(f"{base_url}/inference", data=body, headers={"Content-Type": content_type})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode("utf-8"))["text"].strip()


def detect_device(log: str) -> str:
    match = VULKAN_DEVICE.search(log)
    return match.group(1).strip() if match else "cpu"


def _stop(proc: subprocess.Popen) -> None:
    if proc.poll() is None:
        proc.terminate()
        try:
            proc.wait(timeout=10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=10)


def probe(server_cmd: list[str], model: Path, audio: Path, expect: str, expect_device: str = "",
          no_gpu: bool = False, timeout: float = 120.0, out=sys.stdout) -> ProbeResult:
    port = free_port()
    base_url = f"http://{HOST}:{port}"
    cmd = [*server_cmd, "-m", str(model), "--host", HOST, "--port", str(port)]
    if no_gpu:
        cmd.append("--no-gpu")
    with tempfile.TemporaryFile() as log_file:
        proc = subprocess.Popen(cmd, stdout=log_file, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL)
        try:
            result = _run(proc, base_url, audio, expect, timeout)
        finally:
            _stop(proc)
            log_file.seek(0)
            log = log_file.read().decode("utf-8", errors="replace")
    for line in log.splitlines():
        if DEVICE_LOG.search(line):
            print(f"log: {line.strip()}", file=out)
    result.device = detect_device(log)
    if result.ok and expect_device and expect_device not in log:
        result.ok, result.message = False, f"Gerät {expect_device!r} nicht im Log"
    return result


def _run(proc: subprocess.Popen, base_url: str, audio: Path, expect: str, timeout: float) -> ProbeResult:
    started = time.monotonic()
    while _get_status(f"{base_url}/health") != 200:
        if proc.poll() is not None:
            return ProbeResult(False, f"Server beendet mit Exit {proc.returncode}")
        if time.monotonic() - started > timeout:
            return ProbeResult(False, f"kein /health nach {timeout:.0f} s")
        time.sleep(0.2)
    t0 = time.monotonic()
    try:
        text = transcribe(base_url, audio, timeout)
    except (OSError, ValueError, KeyError) as err:
        return ProbeResult(False, f"/inference fehlgeschlagen: {err.__class__.__name__}")
    ms = int((time.monotonic() - t0) * 1000)
    if expect.lower() not in text.lower():
        return ProbeResult(False, f"Erwartung {expect!r} fehlt", text=text)
    return ProbeResult(True, f"inference_ms={ms}", text=text)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--server", required=True, help="Pfad zu whisper-server[.exe]")
    parser.add_argument("--model", required=True, type=Path)
    parser.add_argument("--audio", required=True, type=Path)
    parser.add_argument("--expect", required=True, help="Teiltext, den die Erkennung enthalten muss")
    parser.add_argument("--expect-device", default="", help="Teiltext, den das Server-Log nennen muss")
    parser.add_argument("--no-gpu", action="store_true")
    parser.add_argument("--timeout", type=float, default=120.0)
    args = parser.parse_args(argv)
    result = probe([args.server], args.model, args.audio, args.expect, args.expect_device, args.no_gpu, args.timeout)
    status = "OK" if result.ok else "FAIL"
    print(f'PROBE {status} {result.message} device="{result.device}" text="{result.text}"')
    return 0 if result.ok else 1


if __name__ == "__main__":
    sys.exit(main())
