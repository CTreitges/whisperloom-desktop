"""Tests für die Suite input (tools/smoke_input.py, tools/smoke_winput.py) — ohne echte Eingaben, ohne App."""
from __future__ import annotations

import io
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke  # noqa: E402
import smoke_input  # noqa: E402
import smoke_winput as wi  # noqa: E402


def obs(text=True, exclude=True, history=True):
    return {"seq": 1, "text": text, "exclude": exclude, "history": history}


class ClipboardPrivateTest(unittest.TestCase):
    def test_all_text_states_excluded_is_ok(self):
        ok, why = wi.clipboard_private([obs(text=False, exclude=False, history=False), obs(), obs()])
        self.assertTrue(ok, why)

    def test_text_without_exclusion_fails(self):
        ok, why = wi.clipboard_private([obs(), obs(exclude=False)])
        self.assertFalse(ok)
        self.assertIn("1 von 2", why)

    def test_history_flag_missing_fails(self):
        self.assertFalse(wi.clipboard_private([obs(history=False)])[0])

    def test_no_text_change_fails(self):
        ok, why = wi.clipboard_private([obs(text=False)])
        self.assertFalse(ok)
        self.assertIn("keine Textänderung", why)


class ForegroundGuardTest(unittest.TestCase):
    """Sicherung aus Lauf 1: ohne Ziel im Vordergrund wird keine Taste gesendet, der Fall meldet FAIL."""

    def keyboard(self, sent: list):
        k = object.__new__(wi.Keyboard)  # ohne user32 (läuft auch unter Linux)
        k.held, k.guard = [], None
        k._key = lambda vk, up: (vk, up)
        k._send = lambda *inputs: sent.extend(inputs)
        return k

    def test_key_is_sent_while_target_is_in_front(self):
        sent: list = []
        k = self.keyboard(sent)
        k.guard = lambda: True
        k.down("ctrl")
        self.assertEqual(sent, [(wi.VK["ctrl"], False)])

    def test_no_key_and_release_of_held_keys_when_target_lost_focus(self):
        sent: list = []
        k = self.keyboard(sent)
        k.down("ctrl")
        k.guard = lambda: False
        with mock.patch.object(wi, "foreground", return_value=0), mock.patch.object(wi, "process_name", return_value="code.exe"):
            with self.assertRaises(wi.NotForeground) as ctx:
                k.down("space")
        self.assertIn("code.exe", str(ctx.exception))
        self.assertNotIn((wi.VK["space"], False), sent)
        self.assertEqual(sent[-1], (wi.VK["ctrl"], True))
        self.assertEqual(k.held, [])

    def test_guarded_case_reports_fail_instead_of_crashing(self):
        report = smoke.Report()

        def case():
            raise wi.NotForeground("vorn: code.exe")

        with redirect_stdout(io.StringIO()) as out:
            smoke_input.guarded(report, ["hold-insert"], case)
        self.assertEqual(report.results, [("hold-insert", False)])
        self.assertIn("SMOKE hold-insert FAIL abgebrochen: vorn: code.exe", out.getvalue())


class InputSuiteGateTest(unittest.TestCase):
    def test_input_without_lock_is_refused(self):
        with tempfile.TemporaryDirectory() as d:
            out = io.StringIO()
            with redirect_stdout(out):
                code = smoke.main(["--platform", "win", "--suite", "input", "--id", "WLD-T", "--app", "x.exe",
                                   "--work-dir", d, "--lock", str(Path(d) / "fehlt.lock")])
        self.assertEqual(code, 3)
        self.assertIn("SMOKE VERWEIGERT input", out.getvalue())

    def test_input_on_linux_is_skipped_with_valid_lock(self):
        with tempfile.TemporaryDirectory() as d:
            lock = Path(d) / "ui-smoke.lock"
            lock.write_text('{"id": "WLD-T", "until": "2999-01-01T00:00:00+00:00"}', encoding="utf-8")
            out = io.StringIO()
            with redirect_stdout(out):
                code = smoke.main(["--platform", "linux", "--suite", "input", "--id", "WLD-T", "--app", "x",
                                   "--work-dir", d, "--lock", str(lock)])
        self.assertEqual(code, 0)
        self.assertIn("SMOKE input SKIP", out.getvalue())


if __name__ == "__main__":
    unittest.main()
