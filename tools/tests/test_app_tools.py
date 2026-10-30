"""Tests für tools/check_i18n.py, tools/fake_api.py und den Lock-/Datenordner-Schutz von tools/smoke.py."""
from __future__ import annotations

import datetime as dt
import io
import json
import sys
import tempfile
import unittest
import urllib.error
import urllib.request
import wave
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import check_i18n  # noqa: E402
import fake_api  # noqa: E402
import smoke  # noqa: E402


def write_app(root: Path, de: dict, en: dict, xaml: str = "", cs: str = "") -> None:
    loc = root / "src/WhisperLoom.App/Localization"
    loc.mkdir(parents=True)
    (loc / "de.json").write_text(json.dumps(de), encoding="utf-8")
    (loc / "en.json").write_text(json.dumps(en), encoding="utf-8")
    (root / "src/WhisperLoom.App/V.axaml").write_text(xaml, encoding="utf-8")
    (root / "src/WhisperLoom.App/C.cs").write_text(cs, encoding="utf-8")


DYN = {k: "x" for k in check_i18n.DYNAMIC}


class CheckI18nTest(unittest.TestCase):
    def run_check(self, de, en, xaml="", cs=""):
        with tempfile.TemporaryDirectory() as d:
            write_app(Path(d), de, en, xaml, cs)
            out = io.StringIO()
            with redirect_stdout(out):
                code = check_i18n.main(["--root", d])
            return code, out.getvalue()

    def test_ok_when_both_languages_have_all_used_keys(self):
        table = {**DYN, "A_b": "x", "C_d": "y"}
        code, out = self.run_check(table, table, '{Binding [A_b], Source={x:Static l:Loc.Instance}}', 'Loc.Instance["C_d"]')
        self.assertEqual(code, 0)
        self.assertEqual(out.strip().splitlines()[-1], f"I18N OK keys={len(table)}")

    def test_missing_translation_used_key_and_empty_value_fail(self):
        code, out = self.run_check({**DYN, "A_b": "x", "E_f": ""}, {**DYN, "E_f": "z"}, "", 'loc["Missing_key"] "Error_Foo"')
        self.assertEqual(code, 1)
        self.assertIn("nur DE: A_b", out)
        self.assertIn("leer (de): E_f", out)
        self.assertIn("fehlt: Missing_key", out)
        self.assertIn("fehlt: Error_Foo", out)

    def test_real_repo_is_consistent(self):
        errors, count = check_i18n.check(Path(__file__).resolve().parents[2])
        self.assertEqual(errors, [])
        self.assertGreater(count, 50)


class FakeApiTest(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.api, cls.proxy, cls.state = fake_api.serve()
        cls.base = f"http://127.0.0.1:{cls.api.server_address[1]}"
        cls.direct = urllib.request.build_opener(urllib.request.ProxyHandler({}))

    @classmethod
    def tearDownClass(cls):
        for s in (cls.api, cls.proxy):
            s.shutdown()
            s.server_close()

    def setUp(self):
        self.post("/_reset", {})

    def post(self, path, payload, headers=None, opener=None):
        req = urllib.request.Request(self.base + path, data=json.dumps(payload).encode(),
                                     headers={"Content-Type": "application/json", **(headers or {})}, method="POST")
        with (opener or self.direct).open(req, timeout=10) as r:
            return r.status, json.loads(r.read())

    def requests(self):
        with self.direct.open(self.base + "/_requests", timeout=10) as r:
            return json.loads(r.read())["requests"]

    def test_stt_records_language_and_returns_text(self):
        boundary = "b0"
        body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"language\"\r\n\r\nen\r\n"
                f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.wav\"\r\n\r\nRIFF\r\n--{boundary}--\r\n").encode()
        req = urllib.request.Request(self.base + "/v1/audio/transcriptions", data=body, method="POST",
                                     headers={"Content-Type": f"multipart/form-data; boundary={boundary}", "Authorization": "Bearer test-key-not-real-1"})
        with self.direct.open(req, timeout=10) as r:
            self.assertEqual(json.loads(r.read())["text"], fake_api.DEFAULT_STT_TEXT)
        last = self.requests()[-1]
        self.assertEqual(last["language"], "en")
        self.assertTrue(last["has_key"])
        self.assertNotIn("test-key-not-real-1", json.dumps(self.requests()))

    def test_chat_and_anthropic_echo_user_text(self):
        _, chat = self.post("/v1/chat/completions", {"model": "m", "messages": [{"role": "system", "content": "You are"}, {"role": "user", "content": "hi"}]})
        self.assertEqual(chat["choices"][0]["message"]["content"], "[fake-llm] hi")
        _, msg = self.post("/anthropic/v1/messages", {"model": "c", "system": "S", "messages": [{"role": "user", "content": "yo"}]})
        self.assertEqual(msg["content"][0]["text"], "[fake-llm] yo")
        self.assertEqual(self.requests()[0]["system_start"], "You are")

    def test_controlled_failure_then_recovery(self):
        self.post("/_control", {"fail": {"/v1/chat/completions": {"status": 503, "count": 1}}})
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            self.post("/v1/chat/completions", {"messages": []})
        self.assertEqual(ctx.exception.code, 503)
        status, _ = self.post("/v1/chat/completions", {"messages": [{"role": "user", "content": "x"}]})
        self.assertEqual(status, 200)

    def test_models_needs_key(self):
        with self.assertRaises(urllib.error.HTTPError) as ctx:
            self.direct.open(self.base + "/v1/models", timeout=10)
        self.assertEqual(ctx.exception.code, 401)

    def test_proxy_marks_requests(self):
        via = urllib.request.build_opener(urllib.request.ProxyHandler({"http": f"http://127.0.0.1:{self.proxy.server_address[1]}"}))
        self.post("/v1/chat/completions", {"messages": [{"role": "user", "content": "p"}]}, opener=via)
        reqs = self.requests()
        self.assertTrue(any(r["path"].startswith("proxy:") for r in reqs))
        self.assertTrue(any(r["path"] == "/v1/chat/completions" and r["via_proxy"] for r in reqs))


class SmokeGuardTest(unittest.TestCase):
    def lock(self, d: Path, data) -> Path:
        p = d / "ui-smoke.lock"
        p.write_text(json.dumps(data), encoding="utf-8")
        return p

    def test_lock_must_match_id_and_be_in_the_future(self):
        now = dt.datetime(2026, 10, 30, 12, 0, tzinfo=dt.timezone.utc)
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            self.assertFalse(smoke.lock_valid(d / "none.lock", "WLD-P2", now)[0])
            self.assertTrue(smoke.lock_valid(self.lock(d, {"id": "WLD-P2", "until": "2026-10-30T13:00:00+00:00"}), "WLD-P2", now)[0])
            self.assertFalse(smoke.lock_valid(self.lock(d, {"id": "WLD-P3", "until": "2026-10-30T13:00:00+00:00"}), "WLD-P2", now)[0])
            self.assertFalse(smoke.lock_valid(self.lock(d, {"id": "WLD-P2", "until": "2026-10-30T11:00:00+00:00"}), "WLD-P2", now)[0])
            self.assertFalse(smoke.lock_valid(self.lock(d, {"id": "WLD-P2", "until": "bald"}), "WLD-P2", now)[0])

    def test_ui_suite_refuses_without_lock(self):
        with tempfile.TemporaryDirectory() as d:
            out = io.StringIO()
            with redirect_stdout(out):
                code = smoke.main(["--platform", "win", "--suite", "ui", "--id", "WLD-P2", "--app", "x.exe", "--lock", str(Path(d) / "no.lock")])
            self.assertEqual(code, 3)
            self.assertIn("SMOKE VERWEIGERT ui", out.getvalue())

    def test_fresh_dir_only_touches_marked_smoke_dirs(self):
        with tempfile.TemporaryDirectory() as d:
            foreign = Path(d) / "foreign"
            foreign.mkdir()
            (foreign / "keep.txt").write_text("x")
            with self.assertRaises(RuntimeError):
                smoke.fresh_dir(foreign)
            self.assertTrue((foreign / "keep.txt").exists())
            mine = Path(d) / "mine"
            smoke.fresh_dir(mine)
            (mine / "old.txt").write_text("x")
            smoke.fresh_dir(mine)
            self.assertFalse((mine / "old.txt").exists())

    def test_test_wav_is_16k_mono(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "t.wav"
            smoke.write_test_wav(p, 0.5)
            with wave.open(str(p)) as w:
                self.assertEqual((w.getframerate(), w.getnchannels(), w.getnframes()), (16000, 1, 8000))


if __name__ == "__main__":
    unittest.main()
