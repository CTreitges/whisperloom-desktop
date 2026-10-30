"""Tests für die Suite ui (tools/smoke.py) und die Tray-Prüfung — ohne echte Zwischenablage, ohne App, ohne Shell."""
from __future__ import annotations

import io
import json
import sys
import tempfile
import types
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke  # noqa: E402
import smoke_platform as plat  # noqa: E402

APP_PID = 4242


class FindTrayIconTest(unittest.TestCase):
    WINDOWS = [
        (0x10, APP_PID, "Chrome_WidgetWin_1"),
        (0x20, 999, "AvaloniaMessageWindow 1111"),  # fremde App (z. B. die echte WhisperLoom-Instanz)
        (0x30, APP_PID, "AvaloniaMessageWindow 2222"),
    ]

    def test_only_the_message_window_of_the_pid_counts(self):
        self.assertEqual(plat.tray_owner_windows(APP_PID, self.WINDOWS), [0x30])

    def test_first_registered_id_wins(self):
        asked = []

        def rect(hwnd, uid):
            asked.append((hwnd, uid))
            return (10, 20, 34, 44) if uid == 2 else None

        self.assertEqual(plat.find_tray_icon(APP_PID, self.WINDOWS, rect), "hwnd=0x30 uid=2 rect=(10, 20, 34, 44)")
        self.assertEqual(asked, [(0x30, 1), (0x30, 2)])

    def test_none_without_registered_icon_or_window(self):
        self.assertIsNone(plat.find_tray_icon(APP_PID, self.WINDOWS, lambda h, u: None))
        self.assertIsNone(plat.find_tray_icon(APP_PID, [(0x20, 999, "AvaloniaMessageWindow x")], lambda h, u: (0, 0, 1, 1)))


class FakeFakeApi:
    base = "http://127.0.0.1:5000"
    port = 5000
    proxy_port = 5001

    def app_base(self, via_proxy):
        return "http://fake-api.invalid:5000" if via_proxy else self.base


class FakeRun:
    """Statt der App: merkt sich Start-Argumente und CLI-Aufrufe, Status wie nach einem Diktat."""

    instances: list[FakeRun] = []
    result = "[fake-llm] This is a fake transcript."

    def __init__(self, app, data_dir, prefix, extra):
        self.args = (app, data_dir, prefix, extra)
        self.calls: list[tuple] = []
        self.proc = types.SimpleNamespace(pid=APP_PID)
        self.stopped = False
        FakeRun.instances.append(self)

    def start(self):
        settings = json.loads((self.args[1] / "settings.json").read_text(encoding="utf-8"))
        self.settings = settings
        return {"running": True}

    def cli(self, *args, stdin=None, timeout=60):
        self.calls.append(args)
        return types.SimpleNamespace(returncode=0, stdout="", stderr="")

    def status(self):
        return {"running": True, "lastResult": self.result}

    def stop(self):
        self.stopped = True


class SuiteUiTest(unittest.TestCase):
    def run_ui(self, platform="win", clipboard=FakeRun.result, tray="hwnd=0x30 uid=1 rect=(0, 0, 1, 1)", result=FakeRun.result):
        FakeRun.instances.clear()
        FakeRun.result = result
        tmp = Path(tempfile.mkdtemp())
        args = types.SimpleNamespace(platform=platform, id="TEST", app="WhisperLoom.exe", data_dir=tmp / "data")
        report = smoke.Report()
        cleanup = mock.Mock()
        with mock.patch.object(smoke, "AppRun", FakeRun), \
                mock.patch.object(smoke.time, "sleep"), \
                mock.patch.object(plat, "read_clipboard", return_value=clipboard), \
                mock.patch.object(plat, "tray_icon", return_value=tray), \
                mock.patch.object(plat, "cleanup_secrets", cleanup), \
                redirect_stdout(io.StringIO()) as out:
            smoke.ui_checks(args, tmp, report, FakeFakeApi())
        return report, FakeRun.instances[0], cleanup, out.getvalue()

    def test_windows_checks_tray_and_real_clipboard_against_the_fake_api(self):
        report, run, cleanup, out = self.run_ui()
        self.assertEqual(report.results, [("tray-visible", True), ("clipboard-real", True)])
        # Echte Pipeline gegen die Fake-API, direkt; keine Ausgabe-Senke → echte Zwischenablage.
        self.assertEqual(run.settings["stt"]["baseUrl"], "http://127.0.0.1:5000/v1")
        self.assertEqual(run.settings["llm"]["providerId"], "custom")
        self.assertNotIn("desktop", run.settings)
        self.assertNotIn("--output-file", run.args[3])
        self.assertEqual([c for c in run.calls if c == ("toggle",)], [("toggle",), ("toggle",)])
        self.assertTrue(run.stopped)
        cleanup.assert_called_once_with("win", "WhisperLoom-Test-TEST:", run.args[1])
        self.assertIn("SMOKE tray-visible OK", out)

    def test_linux_has_no_tray_check(self):
        report, *_ = self.run_ui(platform="linux")
        self.assertEqual(report.results, [("clipboard-real", True)])

    def test_missing_tray_icon_fails(self):
        with mock.patch.object(smoke, "wait_for", side_effect=lambda probe, seconds: probe()):
            report, *_ = self.run_ui(tray=None)
        self.assertEqual(report.results[0], ("tray-visible", False))

    def test_clipboard_must_hold_the_pipeline_result(self):
        self.assertFalse(self.run_ui(clipboard="etwas anderes")[0].results[-1][1])
        # Ohne Ergebnis der Stufe Prompt (z. B. nicht eingerichtet) zählt auch eine leere Zwischenablage nicht.
        self.assertFalse(self.run_ui(clipboard="", result="")[0].results[-1][1])

    def test_wait_for_retries_until_a_value_arrives(self):
        values = iter([None, None, "ok"])
        with mock.patch.object(smoke.time, "sleep"):
            self.assertEqual(smoke.wait_for(lambda: next(values), 5), "ok")


if __name__ == "__main__":
    unittest.main()
