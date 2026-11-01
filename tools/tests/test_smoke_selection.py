"""Tests für die Suite selection (tools/smoke_selection.py, W2-SEL) und den Linux-Teil in smoke_full — ohne echte Eingaben, ohne App."""
from __future__ import annotations

import io
import os
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import smoke  # noqa: E402
import smoke_full  # noqa: E402
import smoke_selection as sel  # noqa: E402


class FakeApiStub:
    proxy_port = 1

    def app_base(self, via_proxy: bool) -> str:
        return "http://127.0.0.1:1"


class SelectionSuiteTest(unittest.TestCase):
    def test_refuses_without_lock(self):
        with tempfile.TemporaryDirectory() as d:
            out = io.StringIO()
            with redirect_stdout(out):
                code = smoke.main(["--platform", "win", "--suite", "selection", "--id", "W2-SEL", "--app", "x.exe",
                                   "--lock", str(Path(d) / "no.lock")])
            self.assertEqual(code, 3)
            self.assertIn("SMOKE VERWEIGERT selection", out.getvalue())

    def test_settings_switch_on_all_three_starters_with_stage_off(self):
        s = sel.selection_settings(FakeApiStub())
        self.assertEqual(s["selection"], {"hotkeyCombo": "Ctrl+Alt+Shift+S", "floatingButton": True, "ctrlRightClick": True})
        self.assertEqual(s["refine"]["stage"], "off")
        self.assertFalse(s["refine"]["promptEnabled"])  # Wähler: Glätten, Verschönern, Zusammenfassen, Anweisung
        self.assertEqual(sel.STAGES, ["polish", "beautify", "summarize", "instruction"])

    def test_every_check_name_is_reported_when_the_target_is_not_in_front(self):
        self.assertEqual(sel.NAMES, ["selection-hotkey", "selection-ctrl-right-click", "selection-button", "selection-clipboard",
                                     "selection-front"])


class FakePrimaryTest(unittest.TestCase):
    @unittest.skipIf(os.name == "nt", "Shell-Skripte nur unter Linux ausführbar")
    def test_fake_tools_print_the_text_and_come_first_in_path(self):
        with tempfile.TemporaryDirectory() as d:
            env = smoke_full.fake_primary_env(Path(d))
            for tool in ("wl-paste", "xclip"):
                r = subprocess.run([tool, "--primary"], env=env, capture_output=True, text=True, check=True)
                self.assertEqual(r.stdout, smoke_full.PRIMARY_TEXT)

    def test_path_starts_with_the_fake_dir(self):
        with tempfile.TemporaryDirectory() as d:
            env = smoke_full.fake_primary_env(Path(d))
            self.assertTrue(env["PATH"].startswith(str(Path(d) / "fakebin") + os.pathsep))
            self.assertIn(smoke_full.PRIMARY_TEXT, (Path(d) / "fakebin" / "wl-paste").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
