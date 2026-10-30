"""Tests für tools/check_provider_parity.py gegen einen künstlichen ProviderCatalog.kt und eine providers.json."""

from __future__ import annotations

import contextlib
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import check_provider_parity  # noqa: E402

KOTLIN = '''
package com.chris.whisperloom.api

object ProviderCatalog {
    const val CATALOG_DATE = "2026-10-08"
    const val OPENAI_ID = "openai"
    private const val MB_25 = 26_214_400

    val providers: List<Provider> = listOf(
        Provider(
            id = OPENAI_ID,
            name = "OpenAI",
            baseUrl = "https://api.openai.com/v1",
            sttMaxBytes = MB_25,
            // Kommentar mit "Anführungszeichen" und (Klammern)
            sttModels = listOf(
                ModelOption("gpt-transcribe", "GPT Transcribe", "\\$0.0045/min.", languageField = "languages[]"),
            ),
            llmModels = listOf(
                ModelOption("gpt-6-luna", "GPT-6 Luna", "kein temperature", temperatureSupported = false, reasoningEffort = "none"),
                ModelOption("gpt-4o-mini", "GPT-4o mini"),
            ),
            notes = "Teil eins " +
                "Teil zwei.",
            rewriteLlmModel = "gpt-4o-mini",
        ),
        Provider(
            id = "ollama",
            name = "Ollama (lokal)",
            baseUrl = "",
            needsKey = false,
            api = ApiStyle.OLLAMA,
        ),
    )

    val custom: Provider = providers.first { it.id == "x" }
}
'''

JSON = {
    "catalogDate": "2026-10-08",
    "providers": [
        {
            "id": "openai",
            "name": "OpenAI",
            "baseUrl": "https://api.openai.com/v1",
            "sttMaxBytes": 26214400,
            "sttModels": [{"id": "gpt-transcribe", "label": "GPT Transcribe", "note": "$0.0045/min.", "languageField": "languages[]"}],
            "llmModels": [
                {"id": "gpt-6-luna", "label": "GPT-6 Luna", "note": "kein temperature", "temperatureSupported": False, "reasoningEffort": "none"},
                {"id": "gpt-4o-mini", "label": "GPT-4o mini"},
            ],
            "notes": "Teil eins Teil zwei.",
            "rewriteLlmModel": "gpt-4o-mini",
        },
        {"id": "ollama", "name": "Ollama (lokal)", "baseUrl": "", "needsKey": False, "api": "OLLAMA"},
    ],
}


class CheckProviderParityTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.android = root / "android"
        kotlin = self.android / check_provider_parity.KOTLIN_PATH
        kotlin.parent.mkdir(parents=True)
        kotlin.write_text(KOTLIN, encoding="utf-8")
        self.json = root / "providers.json"
        self.exceptions = root / "parity-exceptions.md"
        self.write_json(JSON)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def write_json(self, data: dict) -> None:
        self.json.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")

    def run_check(self) -> tuple[int, str]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = check_provider_parity.main([
                "--android", str(self.android), "--json", str(self.json), "--exceptions", str(self.exceptions),
            ])
        return code, out.getvalue()

    def test_gleicher_katalog_ist_ok(self) -> None:
        code, out = self.run_check()
        self.assertEqual(0, code, out)
        self.assertEqual("PROVIDER-PARITY OK providers=2 exceptions=0", out.strip().splitlines()[-1])

    def test_abweichendes_flag_faellt_auf(self) -> None:
        data = json.loads(json.dumps(JSON))
        data["providers"][0]["llmModels"][0]["temperatureSupported"] = True
        self.write_json(data)
        code, out = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("openai.llmModels[gpt-6-luna].temperatureSupported", out)
        self.assertTrue(out.strip().endswith("PROVIDER-PARITY FAIL errors=1"))

    def test_standardmodell_und_reihenfolge_fallen_auf(self) -> None:
        data = json.loads(json.dumps(JSON))
        data["providers"][0]["llmModels"].reverse()
        data["providers"].reverse()
        self.write_json(data)
        code, out = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("openai.llmModels.order", out)
        self.assertIn("providers.order", out)

    def test_fehlender_anbieter_und_protokoll_fallen_auf(self) -> None:
        data = json.loads(json.dumps(JSON))
        data["providers"][1]["api"] = "OPENAI"
        data["providers"].append({"id": "neu", "name": "Neu", "baseUrl": "https://neu"})
        self.write_json(data)
        code, out = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("ollama.api", out)
        self.assertIn("Unterschied ohne Ausnahme: neu", out)

    def test_begruendete_ausnahme_zaehlt(self) -> None:
        data = json.loads(json.dumps(JSON))
        data["providers"][0]["notes"] = "Desktop-Hinweis"
        self.write_json(data)
        self.exceptions.write_text(
            "# Ausnahmen\n\n## Prompts\n\n| Unterschied | Begründung |\n|---|---|\n| x | y |\n\n"
            "## Anbieter\n\n| Unterschied | Begründung |\n|---|---|\n| `openai.notes` | Desktop nennt andere Grenzen |\n",
            encoding="utf-8",
        )
        code, out = self.run_check()
        self.assertEqual(0, code, out)
        self.assertTrue(out.strip().endswith("PROVIDER-PARITY OK providers=2 exceptions=1"))

    def test_ausnahme_ohne_begruendung_oder_ohne_unterschied_ist_ein_fehler(self) -> None:
        self.exceptions.write_text(
            "## Anbieter\n\n| Unterschied | Begründung |\n|---|---|\n| openai.name | |\n", encoding="utf-8")
        code, out = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("Begründung fehlt", out)
        self.assertIn("Ausnahme ohne Unterschied: openai.name", out)

    def test_kotlin_strings_konstanten_und_standardwerte(self) -> None:
        date, providers = check_provider_parity.parse_kotlin(self.android / check_provider_parity.KOTLIN_PATH)
        self.assertEqual("2026-10-08", date)
        openai, ollama = providers
        self.assertEqual(26_214_400, openai["sttMaxBytes"])
        self.assertEqual("$0.0045/min.", openai["sttModels"][0]["note"])
        self.assertEqual("Teil eins Teil zwei.", openai["notes"])
        self.assertTrue(openai["needsKey"])
        self.assertEqual("language", openai["llmModels"][1]["languageField"])
        self.assertEqual("OLLAMA", ollama["api"])
        self.assertEqual(90, ollama["defaultReadTimeoutSec"])

    def test_fehlendes_android_repo(self) -> None:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = check_provider_parity.main(["--android", str(Path(self.tmp.name) / "fehlt"), "--json", str(self.json)])
        self.assertEqual(1, code)
        self.assertIn("PROVIDER-PARITY FAIL", out.getvalue())


if __name__ == "__main__":
    unittest.main()
