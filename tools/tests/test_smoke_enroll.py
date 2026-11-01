"""Tests für die Suite enroll (tools/smoke_enroll.py) und das Aufräumen der Firmen-Secrets (smoke_platform)."""
from __future__ import annotations

import argparse
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import smoke  # noqa: E402
import smoke_enroll as se  # noqa: E402
import smoke_platform as plat  # noqa: E402


class PolicyFieldsTest(unittest.TestCase):
    def test_level_and_value_per_path(self):
        fields = se.policy_fields({"refine.stage": ("fixed", "beautify"), "output.alwaysCopy": ("default", "on")})
        self.assertEqual({"s:refine.stage": "fixed", "w:refine.stage": "beautify",
                          "s:output.alwaysCopy": "default", "w:output.alwaysCopy": "on"}, fields)


class ServerPythonTest(unittest.TestCase):
    def test_explicit_path_wins(self):
        with tempfile.TemporaryDirectory() as d:
            exe = Path(d) / "python.exe"
            exe.write_text("", encoding="utf-8")
            self.assertEqual(str(exe), se.server_python(str(exe)))

    def test_missing_everything_is_an_error(self):
        with mock.patch.object(se, "SERVER_DIR", Path(tempfile.gettempdir()) / "gibt-es-nicht-wl"), \
                mock.patch.dict("os.environ", {"WLD_SERVER_PYTHON": ""}):
            with self.assertRaises(RuntimeError):
                se.server_python(None)


class RunTest(unittest.TestCase):
    def test_without_server_local_the_suite_fails_instead_of_passing(self):
        report = smoke.Report()
        args = argparse.Namespace(server_local=False)
        se.run(args, Path(tempfile.gettempdir()), report, smoke)
        self.assertEqual([("server-local", False)], report.results)

    def test_suite_is_registered(self):
        self.assertIs(smoke.smoke_enroll, se)
        with mock.patch.object(se, "run") as run, mock.patch.object(smoke.Report, "finish", return_value=0):
            smoke.main(["--platform", "win", "--suite", "enroll", "--id", "T", "--app", "x.exe", "--server-local"])
        self.assertTrue(run.called)
        self.assertTrue(run.call_args.args[0].server_local)


class CleanupTest(unittest.TestCase):
    def test_device_token_and_company_keys_are_removed_on_windows(self):
        deleted: list[str] = []
        with mock.patch.object(plat, "win_delete", deleted.append):
            plat.cleanup_secrets("win", "WhisperLoom-Test-ENR:", Path(tempfile.gettempdir()))
        self.assertIn("WhisperLoom-Test-ENR:device", deleted)
        self.assertIn("WhisperLoom-Test-ENR:company:stt", deleted)
        self.assertIn("WhisperLoom-Test-ENR:stt", deleted)
        self.assertTrue(all(d.startswith("WhisperLoom-Test-") for d in deleted))

    def test_linux_clears_both_services(self):
        calls: list[list[str]] = []
        with tempfile.TemporaryDirectory() as d, mock.patch.object(plat.subprocess, "run", lambda a, **k: calls.append(a)):
            plat.cleanup_secrets("linux", "WhisperLoom-Test-ENR:", Path(d))
        services = {c[3] for c in calls}
        self.assertEqual({"WhisperLoom-Test-ENR", "WhisperLoom-Test-ENR:company"}, services)
        self.assertIn(["secret-tool", "clear", "service", "WhisperLoom-Test-ENR", "account", "device"], calls)

    def test_never_without_test_prefix(self):
        with self.assertRaises(ValueError):
            plat.cleanup_secrets("win", "WhisperLoom:", Path(tempfile.gettempdir()))


if __name__ == "__main__":
    unittest.main()
