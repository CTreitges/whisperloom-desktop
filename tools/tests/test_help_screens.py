"""Tests für tools/gen_help_screens.py: fehlende, übrige und veraltete Hilfe-Screenshots werden erkannt; das Repo ist aktuell."""
from __future__ import annotations

import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import gen_help_screens as gen  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
PNG = b"\x89PNG\r\n\x1a\n"


def fake_repo(tmp: Path, screens: list[dict]) -> Path:
    spec = tmp / gen.SPEC
    spec.parent.mkdir(parents=True)
    spec.write_text(json.dumps({"screens": screens}), encoding="utf-8")
    return spec


class HelpScreensTest(unittest.TestCase):
    def test_repository_screens_are_complete_and_current(self):
        self.assertEqual([], gen.check(ROOT))

    def test_expected_files_cover_both_languages_and_themes(self):
        files = gen.expected_files([{"name": "a"}, {"name": "b"}])
        self.assertEqual(8, len(files))
        self.assertIn("de-dark/a.png", files)
        self.assertIn("en-light/b.png", files)

    def test_install_writes_manifest_and_drops_leftovers(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            fake_repo(root, [{"name": "a", "markers": ["name:X"]}])
            old = root / gen.OUT / "de-light" / "alt.png"
            old.parent.mkdir(parents=True)
            old.write_bytes(PNG)
            source = root / "src-out"
            for f in gen.expected_files(gen.load_spec(root / gen.SPEC)):
                (source / f).parent.mkdir(parents=True, exist_ok=True)
                (source / f).write_bytes(PNG)
            self.assertEqual(4, gen.install(source, root))
            self.assertFalse(old.exists(), "übrige Bilder werden entfernt")
            self.assertEqual([], gen.check(root))

    def test_check_reports_missing_leftover_and_stale(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            spec = fake_repo(root, [{"name": "a", "markers": ["name:X"]}])
            source = root / "src-out"
            for f in gen.expected_files(gen.load_spec(spec)):
                (source / f).parent.mkdir(parents=True, exist_ok=True)
                (source / f).write_bytes(PNG)
            gen.install(source, root)
            (root / gen.OUT / "en-dark" / "a.png").unlink()
            (root / gen.OUT / "de-dark" / "b.png").write_bytes(PNG)
            spec.write_text(json.dumps({"screens": [{"name": "a", "markers": ["name:Y"]}]}), encoding="utf-8")
            problems = gen.check(root)
            self.assertTrue(any(p.startswith("fehlt: en-dark/a.png") for p in problems), problems)
            self.assertTrue(any("übrig" in p and "de-dark/b.png" in p for p in problems), problems)
            self.assertTrue(any(p.startswith("veraltet") for p in problems), problems)

    def test_install_refuses_incomplete_output(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            fake_repo(root, [{"name": "a", "markers": ["name:X"]}])
            with self.assertRaises(SystemExit):
                gen.install(root / "leer", root)

    def test_spec_hash_ignores_line_endings(self):
        with tempfile.TemporaryDirectory() as t:
            a, b = Path(t) / "a.json", Path(t) / "b.json"
            a.write_bytes(b'{"screens": []}\n')
            b.write_bytes(b'{"screens": []}\r\n')
            self.assertEqual(gen.spec_hash(a), gen.spec_hash(b))

    def test_every_screen_has_markers_and_unique_name(self):
        with tempfile.TemporaryDirectory() as t:
            root = Path(t)
            fake_repo(root, [{"name": "a", "markers": []}, {"name": "a", "markers": ["name:X"]}])
            problems = gen.check(root)
            self.assertIn("Name doppelt: a", problems)
            self.assertIn("a: keine Markierung", problems)

    def test_all_shipped_help_assets_stay_below_8_mb(self):
        total = sum(p.stat().st_size for p in (ROOT / gen.HELP).rglob("*") if p.is_file())
        self.assertLess(total, 8 * 1024 * 1024, f"{total} Bytes")


if __name__ == "__main__":
    unittest.main()
