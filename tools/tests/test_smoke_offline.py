"""Tests für die Suite offline (tools/smoke_offline.py) — ohne App, ohne Begleitprozess, ohne echtes Ollama."""
from __future__ import annotations

import io
import json
import sys
import types
import unittest
import urllib.request
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke  # noqa: E402
import smoke_offline as off  # noqa: E402


def _open(url: str, payload: dict | None = None) -> dict:
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    data = None if payload is None else json.dumps(payload).encode()
    with opener.open(urllib.request.Request(url, data=data), timeout=5) as r:
        return json.loads(r.read())


class FakeOllamaTest(unittest.TestCase):
    def test_tags_and_chat(self):
        with off.FakeOllama() as fake:
            self.assertTrue(fake.url.startswith("http://127.0.0.1:"))
            tags = _open(fake.url + "/api/tags")
            self.assertEqual([m["name"] for m in tags["models"]], [off.FAKE_MODEL])
            chat = _open(fake.url + "/api/chat", {"model": off.FAKE_MODEL, "messages": [
                {"role": "system", "content": "S"}, {"role": "user", "content": "<dictation>\nand so my fellow\n</dictation>"}]})
            self.assertEqual(chat["message"]["content"], "[fake-ollama] and so my fellow")
            self.assertEqual(fake.chats, [off.FAKE_MODEL])


class SettingsTest(unittest.TestCase):
    def test_offline_tiny_cpu_with_ollama(self):
        s = off.settings("polish", "http://127.0.0.1:1")
        self.assertEqual((s["engine"], s["offline"]["model"], s["refine"]["stage"]), ("offline", "tiny", "polish"))
        self.assertEqual(s["local"], {"backend": "cpu", "ollamaModel": off.FAKE_MODEL, "ollamaUrl": "http://127.0.0.1:1"})


class PidAliveTest(unittest.TestCase):
    def test_linux_reads_comm(self):
        with mock.patch.object(Path, "read_text", return_value="whisper-server\n"):
            self.assertTrue(off.pid_alive("linux", 5))
        with mock.patch.object(Path, "read_text", side_effect=OSError):
            self.assertFalse(off.pid_alive("linux", 5))

    def test_windows_tasklist(self):
        found = types.SimpleNamespace(stdout='"whisper-server.exe","5","Console","1","10 K"\n')
        with mock.patch.object(off.subprocess, "run", return_value=found):
            self.assertTrue(off.pid_alive("win", 5))
        with mock.patch.object(off.subprocess, "run", return_value=types.SimpleNamespace(stdout=None)):
            self.assertFalse(off.pid_alive("win", 5))

    def test_wait_gone_times_out(self):
        with mock.patch.object(off, "pid_alive", return_value=True), mock.patch.object(off.time, "sleep"):
            self.assertFalse(off.wait_gone("win", 5, seconds=0))


class RealOllamaTest(unittest.TestCase):
    def test_only_reads_tags_and_names_small_models(self):
        body = json.dumps({"models": [{"name": "qwen3:1.7b"}, {"name": "gemma3:1b"}, {"name": "llama3.3:70b"}]}).encode()
        resp = mock.MagicMock()
        resp.__enter__.return_value.read.return_value = body
        opener = mock.MagicMock()
        opener.open.return_value = resp
        with mock.patch.object(off.urllib.request, "build_opener", return_value=opener):
            note = off.real_ollama_note()
        self.assertIn("gemma3:1b", note)
        self.assertNotIn("llama3.3", note.split("klein:")[1])
        self.assertEqual(opener.open.call_args[0][0], "http://127.0.0.1:11434/api/tags")


class SuiteChoiceTest(unittest.TestCase):
    def test_offline_needs_no_lock_and_dispatches(self):
        with mock.patch.object(off, "run") as run, redirect_stdout(io.StringIO()):
            rc = smoke.main(["--platform", "win", "--suite", "offline", "--id", "T", "--app", "x"])
        run.assert_called_once()
        self.assertEqual(rc, 1)  # keine Prüfung gemeldet → FAIL 0/0


if __name__ == "__main__":
    unittest.main()
