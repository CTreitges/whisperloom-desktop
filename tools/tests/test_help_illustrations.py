"""Tests für tools/gen_help_illustrations.py: deterministisch, aktuell, nur Loom-Tokens, Bildbeschreibung DE+EN."""
from __future__ import annotations

import json
import re
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import gen_help_illustrations as gen  # noqa: E402
from help_art import Canvas, colors_used, load_tokens, num  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


class HelpIllustrationTest(unittest.TestCase):
    def test_two_runs_give_identical_files(self):
        with tempfile.TemporaryDirectory() as a, tempfile.TemporaryDirectory() as b:
            first = gen.generate(ROOT, Path(a))
            second = gen.generate(ROOT, Path(b))
            self.assertEqual(len(first), len(gen.SCENES))
            for x, y in zip(first, second):
                self.assertEqual(x.read_bytes(), y.read_bytes(), x.name)

    def test_checked_in_files_are_current(self):
        """Veraltete Bilder im Repo (Skript geändert, nicht neu erzeugt) fallen auf; ebenso übrig gebliebene."""
        shipped = ROOT / gen.OUT
        with tempfile.TemporaryDirectory() as tmp:
            for path in gen.generate(ROOT, Path(tmp)):
                target = shipped / path.name
                self.assertTrue(target.exists(), f"fehlt: {target}")
                self.assertEqual(path.read_bytes(), target.read_bytes(), f"veraltet: {target.name} – gen_help_illustrations.py laufen lassen")
        self.assertEqual(sorted(p.stem for p in shipped.glob("*.svg")), sorted(gen.SCENES), "übrige oder fehlende SVGs")

    def test_only_loom_tokens_and_320x240(self):
        colors, _ = load_tokens(ROOT)
        for name in gen.SCENES:
            svg = (ROOT / gen.OUT / f"{name}.svg").read_text(encoding="utf-8")
            self.assertIn('viewBox="0 0 320 240"', svg)
            used = colors_used(svg)
            self.assertGreater(len(used), 3, name)
            # Jede Farbe gehört zu ihrem Token (helle Variante); keine Farbe ohne Token.
            for token, color in used:
                self.assertIn(token, colors, f"{name}: {token}")
                self.assertEqual(colors[token]["light"], color, f"{name}: {token}")
            self.assertEqual(len(re.findall(r"#[0-9A-Fa-f]{6}", svg)), len(used), f"{name}: Farbe ohne Token")
            self.assertNotIn("<text", svg, "Bilder enthalten keinen Text")

    def test_unknown_token_or_icon_is_rejected(self):
        colors, icons = load_tokens(ROOT)
        canvas = Canvas(colors, icons)
        with self.assertRaises(KeyError):
            canvas.rect(0, 0, 10, 10, fill="Pink")
        with self.assertRaises(KeyError):
            canvas.icon("Nope", 0, 0, 24, "Accent")

    def test_tokens_cover_both_themes(self):
        colors, icons = load_tokens(ROOT)
        self.assertEqual(colors["Accent"], {"light": "#0F5B50", "dark": "#6FD9C7"})
        self.assertEqual(colors["Tile.Teal"]["light"], colors["Tile.Teal"]["dark"])
        self.assertIn("Microphone", icons)
        self.assertEqual(55, len(icons))

    def test_number_format_is_stable(self):
        self.assertEqual(num(1.0), "1")
        self.assertEqual(num(2.345), "2.35")
        self.assertEqual(num(-0.001), "0")
        self.assertEqual(num(12.50), "12.5")

    def test_every_illustration_has_alt_text_de_en(self):
        for lang in ("de", "en"):
            table = json.loads((ROOT / "src/WhisperLoom.App/Localization" / f"{lang}.json").read_text(encoding="utf-8"))
            for name in gen.SCENES:
                self.assertTrue(table.get(f"HelpImg_{name}", "").strip(), f"{lang}: HelpImg_{name}")


if __name__ == "__main__":
    unittest.main()
