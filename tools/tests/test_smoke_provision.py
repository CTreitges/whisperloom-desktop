"""Tests für die Prüf-Helfer der Suite provision (tools/smoke_provision.py)."""
from __future__ import annotations

import json
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import smoke  # noqa: E402
import smoke_provision as sp  # noqa: E402

GOOD_STATUS = {
    "policy": {"active": True, "locked": list(sp.LOCKED_EXPECTED) + ["llm.providerId"], "historyEnabled": False,
               "updatesEnabled": False, "allowedProviders": ["groq", "openai"]},
    "history": {"enabled": False},
}


class PolicyProblemsTest(unittest.TestCase):
    def test_status_after_fixture_provisioning_is_fine(self):
        self.assertEqual([], sp.policy_problems(GOOD_STATUS))

    def test_missing_lock_and_history_on_are_reported(self):
        bad = json.loads(json.dumps(GOOD_STATUS))
        bad["policy"]["locked"] = ["stt.providerId"]
        bad["history"]["enabled"] = True
        problems = sp.policy_problems(bad)
        self.assertTrue(any("refine.stage" in p for p in problems))
        self.assertIn("Verlauf nicht per Richtlinie aus", problems)

    def test_no_policy_at_all(self):
        self.assertIn("policy nicht aktiv", sp.policy_problems({}))


class ExportProblemsTest(unittest.TestCase):
    def test_clean_export(self):
        self.assertEqual([], sp.export_problems(json.dumps({"schemaVersion": 1, "stt": {"providerId": "groq"}}), "groq"))

    def test_secrets_policy_and_key_values_are_reported(self):
        text = json.dumps({"schemaVersion": 1, "stt": {"providerId": "groq"}, "secrets": {"groq": "test-key-not-real-1"}, "policy": {}})
        problems = sp.export_problems(text, "groq")
        self.assertIn("Abschnitt secrets im Export", problems)
        self.assertIn("Abschnitt policy im Export", problems)
        self.assertIn("Key-Wert im Export", problems)

    def test_not_json(self):
        self.assertEqual(["Export ist kein JSON"], sp.export_problems("{", "groq"))


class FixtureTest(unittest.TestCase):
    def test_fixture_keys_are_fake_keys(self):
        keys = sp.fixture_keys()
        self.assertEqual({"groq", "openai"}, set(keys))
        self.assertTrue(all(v.startswith("test-key-not-real-") for v in keys.values()))

    def test_suite_is_registered(self):
        self.assertIs(smoke.smoke_provision, sp)


if __name__ == "__main__":
    unittest.main()
