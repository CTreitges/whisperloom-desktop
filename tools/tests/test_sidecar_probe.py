"""Tests für tools/sidecar_probe.py gegen einen Fake-whisper-server (Python, gleiche CLI und Endpunkte)."""

from __future__ import annotations

import io
import socket
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import sidecar_probe  # noqa: E402

# Verhalten über den Modellnamen: "crash" → Exit 3 vor dem Bind, sonst Antwort-Text = Inhalt der Modelldatei.
FAKE_SERVER = r'''
import argparse, http.server, json, sys
p = argparse.ArgumentParser()
p.add_argument("-m"); p.add_argument("--host"); p.add_argument("--port", type=int)
p.add_argument("--no-gpu", action="store_true")
a = p.parse_args()
if a.m.endswith("crash"):
    sys.exit(3)
text = open(a.m, encoding="utf-8").read()
print("ggml_vulkan: 0 = Fake GPU 9000 (Fake) | uma: 0" if not a.no_gpu else "using CPU backend", flush=True)
class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def do_GET(self):
        self.send_response(200 if self.path == "/health" else 404); self.end_headers()
        self.wfile.write(b'{"status":"ok"}')
    def do_POST(self):
        body = self.rfile.read(int(self.headers["Content-Length"]))
        ok = self.path == "/inference" and b'name="file"' in body and b"RIFF" in body
        self.send_response(200 if ok else 400); self.end_headers()
        self.wfile.write(json.dumps({"text": text}).encode())
http.server.HTTPServer((a.host, a.port), H).serve_forever()
'''


class SidecarProbeTest(unittest.TestCase):
    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.dir = Path(tmp.name)
        (self.dir / "fake_server.py").write_text(FAKE_SERVER, encoding="utf-8")
        self.cmd = [sys.executable, str(self.dir / "fake_server.py")]
        self.audio = self.dir / "jfk.wav"
        self.audio.write_bytes(b"RIFF\x00\x00\x00\x00WAVE")
        self.model = self.dir / "ggml-fake.bin"
        self.model.write_text(" And so my fellow Americans, ask not.", encoding="utf-8")

    def run_probe(self, **kwargs) -> tuple[sidecar_probe.ProbeResult, str]:
        out = io.StringIO()
        args = {"model": self.model, "audio": self.audio, "expect": "fellow Americans", "timeout": 20, **kwargs}
        return sidecar_probe.probe(self.cmd, out=out, **args), out.getvalue()

    def test_ok_with_expected_text_and_vulkan_device(self) -> None:
        result, log = self.run_probe(expect_device="Fake GPU 9000")
        self.assertTrue(result.ok, result.message)
        self.assertEqual(result.device, "Fake GPU 9000")
        self.assertEqual(result.text, "And so my fellow Americans, ask not.")
        self.assertIn("log: ggml_vulkan: 0 = Fake GPU 9000", log)

    def test_expectation_is_case_insensitive(self) -> None:
        result, _ = self.run_probe(expect="FELLOW americans")
        self.assertTrue(result.ok, result.message)

    def test_fail_on_wrong_expected_text(self) -> None:
        result, _ = self.run_probe(expect="ich bin ein Berliner")
        self.assertFalse(result.ok)
        self.assertIn("Erwartung", result.message)

    def test_fail_when_device_missing_from_log(self) -> None:
        result, _ = self.run_probe(expect_device="GTX 1080 Ti", no_gpu=True)
        self.assertFalse(result.ok)
        self.assertEqual(result.device, "cpu")
        self.assertIn("GTX 1080 Ti", result.message)

    def test_fail_fast_when_server_exits(self) -> None:
        self.model = self.dir / "crash"
        result, _ = self.run_probe()
        self.assertFalse(result.ok)
        self.assertEqual(result.message, "Server beendet mit Exit 3")

    def test_server_is_stopped_after_probe(self) -> None:
        ports = []
        real_free_port = sidecar_probe.free_port

        def record() -> int:
            ports.append(real_free_port())
            return ports[-1]

        sidecar_probe.free_port = record
        self.addCleanup(setattr, sidecar_probe, "free_port", real_free_port)
        result, _ = self.run_probe()
        self.assertTrue(result.ok, result.message)
        with self.assertRaises(OSError):
            socket.create_connection((sidecar_probe.HOST, ports[0]), timeout=1).close()

    def test_main_prints_status_line(self) -> None:
        out = io.StringIO()
        sys_stdout, sys.stdout = sys.stdout, out
        self.addCleanup(setattr, sys, "stdout", sys_stdout)
        code = sidecar_probe.main(["--server", sys.executable, "--model", str(self.model),
                                   "--audio", str(self.audio), "--expect", "x"])
        sys.stdout = sys_stdout
        # sys.executable ist kein Server: endet sofort mit Fehler, Probe meldet FAIL.
        self.assertEqual(code, 1)
        self.assertTrue(out.getvalue().splitlines()[-1].startswith("PROBE FAIL Server beendet"))


if __name__ == "__main__":
    unittest.main()
