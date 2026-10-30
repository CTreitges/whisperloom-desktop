"""Tests für tools/check_prompt_parity.py gegen ein künstliches RefinePrompt.kt und künstliche Ressourcen."""

from __future__ import annotations

import contextlib
import io
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))

import check_prompt_parity as cpp  # noqa: E402

# Alle Formen aus RefinePrompt.kt: Kommentare mit Anführungszeichen, $FRAME, ${if …}, listOfNotNull mit null,
# Raw-String mit trimIndent und eingerückter Unterliste, "+"-Verkettung mit $TAG, ausgeschlossene Literale.
KOTLIN = r'''
package com.chris.whisperloom.api

/** KDoc mit "Stil bewahren" und `<text>` — zählt nicht. */
object RefinePrompt {
    fun build(mode: RefineMode, german: Boolean): String {
        require(mode != RefineMode.OFF) { "RefineMode.OFF hat keine Anweisung" }
        return ""
    }

    fun userText(raw: String, german: Boolean): String = "<$TAG_DE>\n$raw\n</$TAG_DE>"

    private const val TAG_DE = "diktat"
    private val WHITESPACE = Regex("\\s+")

    private object De {
        private const val FRAME = "Rahmen mit „Zitat“ und \"Ansprache\"."

        fun polish(p: Boolean, f: Boolean) = listOfNotNull(
            "Du korrigierst nur ${if (p) "Satzzeichen und Absätze" else "Satzzeichen"}, nie Wörter${if (f) " außer Füllwörtern" else ""}. $FRAME",
            if (f) "- Lass Füllwörter weg." else null,
            // Kommentar mit "?" zählt nicht.
            "Beispiele:",
        ).joinToString("\n")

        fun readable(p: Boolean) = """
            Erste Zeile. $FRAME
            Regeln:
              - Eingerückt bleibt eingerückt.
            ${if (p) "Trenne" else "Schreib alles in einen Absatz. Trenne"} mit Punkt.
        """.trimIndent()

        val reply = "\n" +
            "Antworte nur mit dem Text."
        val replyPrompt = "\n\nAntworte nur mit dem Prompt, ohne " +
            "die Markierung <$TAG_DE>."
    }
}
'''

RESOURCES = {
    "de/frame.txt": "Rahmen mit „Zitat“ und \"Ansprache\".\n",
    "de/polish.txt": (
        "Du korrigierst nur {{p?Satzzeichen und Absätze|Satzzeichen}}, nie Wörter{{f? außer Füllwörtern|}}. {{FRAME}}\n"
        "{{f?- Lass Füllwörter weg.|}}\n"
        "Beispiele:\n"
    ),
    "de/readable.txt": (
        "Erste Zeile. {{FRAME}}\n"
        "Regeln:\n"
        "  - Eingerückt bleibt eingerückt.\n"
        "{{p?Trenne|Schreib alles in einen Absatz. Trenne}} mit Punkt.\n"
    ),
    "de/reply.txt": "Antworte nur mit dem Text.\n",
    "de/reply-prompt.txt": "Antworte nur mit dem Prompt, ohne die Markierung <diktat>.\n",
}

FRAGMENTS = 17


class CheckPromptParityTest(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.android = root / "android"
        self.desktop = root / "desktop"
        kt = self.android / cpp.KOTLIN_FILE
        kt.parent.mkdir(parents=True)
        kt.write_text(KOTLIN, encoding="utf-8")
        self.resources = self.desktop / cpp.RESOURCE_DIR
        for name, text in RESOURCES.items():
            self.write_resource(name, text)

    def tearDown(self) -> None:
        self.tmp.cleanup()

    def write_resource(self, name: str, text: str, newline: str = "\n") -> None:
        path = self.resources / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8", newline=newline)

    def write_exceptions(self, rows: str) -> None:
        docs = self.desktop / "docs"
        docs.mkdir(exist_ok=True)
        (docs / "parity-exceptions.md").write_text(
            "# Paritäts-Ausnahmen\n\n## Prompts\n\n| Fragment | Seite | Begründung |\n|---|---|---|\n" + rows
            + "\n## Anbieter\n\n| Fragment | Seite | Begründung |\n|---|---|---|\n| fremd | android | anderer Abschnitt |\n",
            encoding="utf-8",
        )

    def run_check(self) -> tuple[int, list[str]]:
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            code = cpp.main(["--android", str(self.android), "--desktop", str(self.desktop)])
        return code, out.getvalue().strip().splitlines()

    def test_gleiche_fragmente_sind_ok(self) -> None:
        code, lines = self.run_check()
        self.assertEqual(0, code, lines)
        self.assertEqual(f"PROMPT-PARITY OK prompts={FRAGMENTS} exceptions=0", lines[-1])

    def test_kotlin_fragmente_ohne_kommentare_und_ausgeschlossene_literale(self) -> None:
        fragments = cpp.kotlin_fragments((self.android / cpp.KOTLIN_FILE).read_text(encoding="utf-8"))
        self.assertIn("Rahmen mit „Zitat“ und \"Ansprache\".", fragments)
        self.assertIn("Antworte nur mit dem Prompt, ohne die Markierung <diktat>.", fragments)  # + und $TAG_DE
        self.assertIn("  - Eingerückt bleibt eingerückt.", fragments)  # trimIndent lässt die Unterliste eingerückt
        self.assertIn(" mit Punkt.", fragments)  # Naht nach ${…} am Zeilenanfang
        self.assertIn(" außer Füllwörtern", fragments)
        for foreign in ("Stil bewahren", "RefineMode.OFF hat keine Anweisung", "\\s+", "diktat", "Kommentar"):
            self.assertFalse(any(foreign == f or foreign in f and "Markierung" not in f for f in fragments), foreign)

    def test_ein_geaendertes_zeichen_faellt_auf(self) -> None:
        self.write_resource("de/reply.txt", "Antworte nur mit dem Text!\n")
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("fehlt in C# (1×): Antworte nur mit dem Text.", lines)
        self.assertIn("nur in C# (1×, de/reply.txt): Antworte nur mit dem Text!", lines)
        self.assertTrue(lines[-1].startswith("PROMPT-PARITY FAIL"))

    def test_fehlende_bedingung_faellt_auf(self) -> None:
        self.write_resource("de/polish.txt", RESOURCES["de/polish.txt"].replace("{{f?- Lass Füllwörter weg.|}}\n", ""))
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("fehlt in C# (1×): - Lass Füllwörter weg.", lines)

    def test_leerzeichen_an_der_naht_zaehlt(self) -> None:
        self.write_resource("de/polish.txt", RESOURCES["de/polish.txt"].replace("{{f? außer", "{{f?außer"))
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("nur in C# (1×, de/polish.txt): außer Füllwörtern", lines)

    def test_einrueckung_der_unterliste_zaehlt(self) -> None:
        self.write_resource("de/readable.txt", RESOURCES["de/readable.txt"].replace("  - Eingerückt", "- Eingerückt"))
        code, _ = self.run_check()
        self.assertEqual(1, code)

    def test_doppeltes_fragment_in_csharp_faellt_auf(self) -> None:
        self.write_resource("de/extra.txt", "Beispiele:\n")
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("nur in C# (1×, de/extra.txt, de/polish.txt): Beispiele:", lines)

    def test_crlf_ressourcen_sind_gleich(self) -> None:
        # Git mit autocrlf checkt die .txt unter Windows mit CRLF aus.
        for name, text in RESOURCES.items():
            self.write_resource(name, text, newline="\r\n")
        code, lines = self.run_check()
        self.assertEqual(0, code, lines)

    def test_begruendete_ausnahmen_auf_beiden_seiten(self) -> None:
        self.write_resource("de/reply.txt", "Antworte nur mit dem Text!\n")
        self.write_exceptions(
            "| Antworte nur mit dem Text. | android | Desktop formuliert eigen |\n"
            "| `Antworte nur mit dem Text!` | desktop | Desktop formuliert eigen |\n"
        )
        code, lines = self.run_check()
        self.assertEqual(0, code, lines)
        self.assertEqual(f"PROMPT-PARITY OK prompts={FRAGMENTS} exceptions=2", lines[-1])

    def test_ungenutzte_oder_unbegruendete_ausnahme_ist_ein_fehler(self) -> None:
        self.write_exceptions("| Gibt es nicht. | android | veraltet |\n")
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertIn("Ausnahme ungenutzt (android): Gibt es nicht.", lines)

        self.write_exceptions("| Antworte nur mit dem Text. | android | |\n")
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertTrue(any("Begründung fehlt" in line for line in lines), lines)

    def test_fehlende_quellen_sind_ein_fehler(self) -> None:
        (self.android / cpp.KOTLIN_FILE).unlink()
        code, lines = self.run_check()
        self.assertEqual(1, code)
        self.assertTrue(lines[0].startswith("Kotlin-Datei fehlt"), lines)


if __name__ == "__main__":
    unittest.main()
