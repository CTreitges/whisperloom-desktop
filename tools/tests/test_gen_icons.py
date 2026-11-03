"""Tests für tools/gen_icons.py: Pfade werden eindeutig getrennt, gefüllte Punkte werden zu deckenden Ringen, das Repo ist aktuell."""
from __future__ import annotations

import io
import re
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import gen_icons as gen  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
SVG = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">{}</svg>'


def run(*argv: str) -> tuple[int, str]:
    out = io.StringIO()
    with redirect_stdout(out):
        code = gen.main(list(argv))
    return code, out.getvalue().strip().splitlines()[-1]


class NormalizePathTest(unittest.TestCase):
    def test_compact_numbers_are_split(self):
        self.assertEqual("c -0.8 0.4 -1.1 0.9 -1.1 1.6 v 0.5", gen.normalize_path("c-.8.4-1.1.9-1.1 1.6v.5"))

    def test_arc_keeps_flags_and_absolute_commands(self):
        self.assertEqual("M 12 3 a 9 9 0 1 0 0 18 z", gen.normalize_path("M12 3a9 9 0 1 0 0 18z"))

    def test_wrong_argument_count_is_an_error(self):
        with self.assertRaises(ValueError):
            gen.normalize_path("M12 3 L4")

    def test_unknown_characters_are_an_error(self):
        with self.assertRaises(ValueError):
            gen.normalize_path("M12 3 X4 4")


class DiskTest(unittest.TestCase):
    def covered(self, r: float) -> list[tuple[float, float]]:
        radii = [float(re.search(r"A (\S+)", ring).group(1)) for ring in gen.disk(12, 12, r)]
        half = gen.STROKE / 2
        return sorted((max(0.0, rho - half), rho + half) for rho in radii)

    def assert_full_disk(self, r: float):
        spans = self.covered(r)
        self.assertEqual(0.0, spans[0][0])
        self.assertAlmostEqual(r, spans[-1][1], places=3)
        for (_, top), (bottom, _) in zip(spans, spans[1:]):
            self.assertLessEqual(bottom, top, f"Lücke bei r={r}: {spans}")

    def test_small_dot_is_one_ring(self):
        self.assertEqual(1, len(gen.disk(12, 12, 1)))
        self.assert_full_disk(1)

    def test_large_dot_is_covered_without_gaps(self):
        for r in (0.9, 1.25, 3, 5.5):
            self.assert_full_disk(r)


class GeometryTest(unittest.TestCase):
    def test_paths_and_filled_circles(self):
        text = gen.geometry(SVG.format('<path d="M5 12.5l4.5 4.5L19 7"/><circle cx="12" cy="17" r="1" fill="currentColor"/>'))
        self.assertTrue(text.startswith("M 5 12.5 l 4.5 4.5 L 19 7 M "))
        self.assertIn(" A 0.125 0.125 0 1 0 ", text)

    def test_other_elements_and_view_boxes_are_rejected(self):
        with self.assertRaises(ValueError):
            gen.geometry(SVG.format('<rect x="1" y="1" width="2" height="2"/>'))
        with self.assertRaises(ValueError):
            gen.geometry('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16"><path d="M1 1h2"/></svg>')


class RepositoryTest(unittest.TestCase):
    def test_every_source_has_a_name_and_the_output_is_current(self):
        self.assertEqual(sorted(gen.NAMES), sorted(p.stem for p in (ROOT / gen.SOURCE).glob("*.svg")))
        self.assertEqual((0, f"ICONS OK {len(gen.NAMES)}"), run("--root", str(ROOT), "--check"))

    def test_check_reports_stale_output_and_generation_fixes_it(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / gen.SOURCE).mkdir(parents=True)
            (root / gen.TARGET).parent.mkdir(parents=True)
            for stem in gen.NAMES:
                (root / gen.SOURCE / f"{stem}.svg").write_text(SVG.format('<path d="M3 3h18"/>'), encoding="utf-8")
            code, line = run("--root", tmp, "--check")
            self.assertEqual(1, code)
            self.assertIn("veraltet", line)
            self.assertEqual((0, f"ICONS OK {len(gen.NAMES)}"), run("--root", tmp))
            self.assertEqual(0, run("--root", tmp, "--check")[0])

    def test_missing_source_is_reported(self):
        with tempfile.TemporaryDirectory() as tmp:
            (Path(tmp) / gen.SOURCE).mkdir(parents=True)
            code, line = run("--root", tmp, "--check")
            self.assertEqual(1, code)
            self.assertIn("fehlt", line)


if __name__ == "__main__":
    unittest.main()
