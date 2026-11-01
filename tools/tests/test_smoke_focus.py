"""Tests für tools/smoke_focus.py (W2-OUT) ohne echte Eingabe: Zwischenablage und App sind Fakes."""
from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import smoke_focus  # noqa: E402


class FakeReport:
    def __init__(self):
        self.results = []

    def check(self, name, ok, reason=""):
        self.results.append((name, bool(ok), reason))
        return ok


class FakeRun:
    """App-Fake: history-copy legt den neuesten Text in die (Fake-)Zwischenablage, ab dem n-ten Aufruf nicht mehr."""

    def __init__(self, clip: dict, latest: str, broken_from: int | None = None):
        self.clip, self.latest, self.broken_from, self.calls = clip, latest, broken_from, 0

    def status(self):
        return {"history": {"latest": {"text": self.latest}}, "copies": {"ok": self.calls}}

    def cli(self, *args):
        self.calls += 1
        if self.broken_from is None or self.calls < self.broken_from:
            self.clip["text"] = self.latest
        return subprocess.CompletedProcess(args, 0, "copied\n", "")


class HistoryCopyTest(unittest.TestCase):
    def run_check(self, run, clip):
        report = FakeReport()
        with mock.patch.object(smoke_focus.wi, "set_clipboard", lambda t: clip.__setitem__("text", t)), \
                mock.patch.object(smoke_focus.plat, "read_clipboard", lambda _p: clip["text"]):
            smoke_focus.history_copy_check(run, report)
        return report.results

    def test_twenty_good_copies_pass(self):
        clip = {"text": ""}
        run = FakeRun(clip, "Hallo Welt")
        [(name, ok, _)] = self.run_check(run, clip)
        self.assertEqual(("history-copy-20", True), (name, ok))
        self.assertEqual(20, run.calls)

    def test_one_failed_copy_fails_the_check(self):
        clip = {"text": ""}
        [(_, ok, reason)] = self.run_check(FakeRun(clip, "Hallo Welt", broken_from=7), clip)
        self.assertFalse(ok)
        self.assertIn("#6", reason)

    def test_empty_history_fails(self):
        clip = {"text": ""}
        [(_, ok, _)] = self.run_check(FakeRun(clip, ""), clip)
        self.assertFalse(ok)


class PagesTest(unittest.TestCase):
    def test_measure_covers_field_password_button_and_page(self):
        self.assertEqual({"textarea", "password", "button", "page"}, set(smoke_focus.PAGES))
        self.assertIn("type=password", smoke_focus.PAGES["password"])


if __name__ == "__main__":
    unittest.main()
