"""Tests für die Suite input (tools/smoke_input.py, tools/smoke_winput.py) — ohne echte Eingaben, ohne App."""
from __future__ import annotations

import io
import sys
import tempfile
import types
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

    def mouse(self, sent: list):
        k = self.keyboard(sent)
        k.user32 = types.SimpleNamespace(GetSystemMetrics=lambda i: {76: 0, 77: 0, 78: 1920, 79: 1080}[i])
        k.INPUT = lambda kind, union: ("mouse", union)
        k.INPUT._fields_ = [None, (None, lambda mi: mi)]
        k.MOUSEINPUT = lambda *a: a
        return k

    def test_click_on_a_foreign_window_is_refused_without_input(self):
        sent: list = []
        k = self.mouse(sent)
        with mock.patch.object(wi, "root_window_at", return_value=0x99), \
                mock.patch.object(wi, "process_name", return_value="code.exe"), mock.patch.object(wi.time, "sleep"):
            with self.assertRaises(wi.NotForeground) as ctx:
                k.click(100, 200, allowed=lambda root: root == 0x42)
        self.assertIn("code.exe", str(ctx.exception))
        self.assertEqual(sent, [])

    def test_click_on_the_allowed_window_is_sent(self):
        sent: list = []
        k = self.mouse(sent)
        with mock.patch.object(wi, "root_window_at", return_value=0x42), mock.patch.object(wi.time, "sleep"):
            k.click(100, 200, allowed=lambda root: root == 0x42)
        self.assertEqual(len(sent), 3)  # bewegen, drücken, loslassen

    def test_click_is_refused_when_a_window_appears_after_the_move(self):
        sent: list = []
        k = self.mouse(sent)
        roots = iter([0x42, 0x99])
        with mock.patch.object(wi, "root_window_at", side_effect=lambda x, y: next(roots)), \
                mock.patch.object(wi, "process_name", return_value="code.exe"), mock.patch.object(wi.time, "sleep"):
            with self.assertRaises(wi.NotForeground):
                k.click(100, 200, allowed=lambda root: root == 0x42)
        self.assertEqual(len(sent), 1)  # nur die Bewegung, kein Drücken

    def test_focus_does_not_click_blindly_onto_a_covering_window(self):
        sent: list = []
        k = self.mouse(sent)
        target = smoke_input.Target("x.exe", Path("t.json"), k)
        target.hwnd = 0x42
        with mock.patch.object(wi, "force_foreground"), mock.patch.object(wi, "foreground", return_value=0x99), \
                mock.patch.object(wi, "window_rect", return_value=(0, 0, 200, 100)), \
                mock.patch.object(wi, "root_window_at", return_value=0x99), \
                mock.patch.object(wi, "process_name", return_value="code.exe"), \
                mock.patch.object(smoke_input, "wait_for", return_value=None), mock.patch.object(wi.time, "sleep"):
            with self.assertRaises(wi.NotForeground):
                target.focus()
        self.assertEqual(sent, [])

    def test_guarded_case_reports_fail_instead_of_crashing(self):
        report = smoke.Report()

        def case():
            raise wi.NotForeground("vorn: code.exe")

        with redirect_stdout(io.StringIO()) as out:
            smoke_input.guarded(report, ["hold-insert"], case)
        self.assertEqual(report.results, [("hold-insert", False)])
        self.assertIn("SMOKE hold-insert FAIL abgebrochen: vorn: code.exe", out.getvalue())


class InsertTargetTest(unittest.TestCase):
    """Prüfrunde 2: Der Smoke wird rot, wenn die App nicht ins TestTarget eingefügt hat, und wartet auf dessen Bereit-Signal."""

    @staticmethod
    def status(**window):
        return {"input": {"lastInsertWindow": window or None}}

    def test_insert_into_the_test_target_is_ok(self):
        ok, where = smoke_input.went_to(self.status(hwnd=0x42, sameTarget=True, process="WhisperLoom.TestTarget",
                                                    focusClass="WindowsForms10.EDIT.app.0"), 0x42)
        self.assertTrue(ok)
        self.assertIn("WhisperLoom.TestTarget", where)

    def test_insert_into_another_window_fails_and_names_it(self):
        ok, where = smoke_input.went_to(self.status(hwnd=0x99, sameTarget=False, process="Code", focusClass="Chrome_WidgetWin_1"), 0x42)
        self.assertFalse(ok)
        self.assertIn("Code", where)

    def test_same_hwnd_but_not_the_remembered_target_fails(self):
        self.assertFalse(smoke_input.went_to(self.status(hwnd=0x42, sameTarget=False, process="x"), 0x42)[0])

    def test_no_reported_insert_fails(self):
        ok, where = smoke_input.went_to({"input": {}}, 0x42)
        self.assertFalse(ok)
        self.assertIn("keine Eingabe", where)

    def test_inserted_marks_landed_only_with_text_and_app_report(self):
        run = types.SimpleNamespace(status=lambda: {"state": "idle", "lastResult": "Hallo", "input": {
            "mode": "none", "lastInsertWindow": {"hwnd": 0x42, "sameTarget": True, "process": "WhisperLoom.TestTarget"}}})
        target = types.SimpleNamespace(hwnd=0x42, read=lambda: {"text": "Hallo"})
        _, d = smoke_input.inserted(target, run)
        self.assertTrue(d["_landed"])

        other = types.SimpleNamespace(hwnd=0x43, read=lambda: {"text": "Hallo"})
        _, d = smoke_input.inserted(other, run)
        self.assertFalse(d["_landed"])

    def test_target_is_ready_only_after_shown_with_hwnd(self):
        self.assertFalse(smoke_input.target_ready({}))
        self.assertFalse(smoke_input.target_ready({"hwnd": 0x42}))
        self.assertFalse(smoke_input.target_ready({"ready": True}))
        self.assertTrue(smoke_input.target_ready({"hwnd": 0x42, "ready": True}))

    def test_focus_waits_for_the_focused_text_box(self):
        sent: list = []
        k = ForegroundGuardTest.mouse(ForegroundGuardTest(), sent)
        target = smoke_input.Target("x.exe", Path("t.json"), k)
        target.hwnd = 0x42
        states = iter([{"focused": False}] * 2 + [{"focused": True}] * 10)  # 1. Prüfung nach Vordergrund, 2. nach Klick
        target.read = lambda: next(states)

        def wait_for(probe, seconds, step=0.1):
            return probe()

        with mock.patch.object(wi, "force_foreground"), mock.patch.object(wi, "foreground", return_value=0x42), \
                mock.patch.object(wi, "window_rect", return_value=(0, 0, 200, 100)), \
                mock.patch.object(wi, "root_window_at", return_value=0x42), \
                mock.patch.object(smoke_input, "wait_for", side_effect=wait_for), mock.patch.object(wi.time, "sleep"):
            target.focus()
        self.assertEqual(len(sent), 3)  # vorn, aber Textfeld ohne Fokus → genau ein Klick ins Textfeld


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
