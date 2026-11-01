"""Erzeugt die Screenshots der Hilfe neu: startet die Headless-Screenshot-Tests und legt die PNGs je Sprache und Variante ab.

Aufruf:  py -3 -X utf8 tools/gen_help_screens.py [--root <repo>] [--no-build]
Prüfen:  py -3 -X utf8 tools/gen_help_screens.py --check     (nur prüfen, nichts erzeugen)
Letzte Zeile: "HELP SCREENS OK <anzahl>" bzw. "HELP SCREENS FEHLER <anzahl>" (Exit 1).

Ablauf: Die Liste samt Markierungen steht in src/WhisperLoom.App/Assets/Help/screens.json. Das Skript übergibt sie an
``HelpScreensTest`` (Env ``WLD_HELP_SCREENS_SPEC``), das jede Szene mit Fake-Daten aufbaut und die Markierungen (Rahmen und
Ziffer je Eintrag in ``markers``) beim Rendern einzeichnet – Positionen aus dem echten Layout. Ergebnis in einem Temp-Ordner,
dann ersetzt es ``Assets/Help/screens/<de|en>-<light|dark>/`` vollständig und schreibt ``manifest.json`` mit dem Hash der Liste.
``--check`` meldet fehlende, übrige und veraltete Bilder (Liste geändert, nicht neu erzeugt).
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HELP = Path("src/WhisperLoom.App/Assets/Help")
SPEC = HELP / "screens.json"
OUT = HELP / "screens"
LANGS = ("de", "en")
THEMES = ("light", "dark")
TEST_PROJECT = Path("tests/WhisperLoom.App.Tests")
TEST_FILTER = "FullyQualifiedName~HelpScreensTest.Generate_help_screens_for_every_language_and_theme"


def load_spec(path: Path) -> list[dict]:
    return json.loads(path.read_text(encoding="utf-8"))["screens"]


def spec_hash(path: Path) -> str:
    """Hash der Liste ohne Zeilenende-Unterschiede (autocrlf)."""
    return hashlib.sha256(path.read_bytes().replace(b"\r\n", b"\n")).hexdigest()


def expected_files(screens: list[dict]) -> list[str]:
    return sorted(f"{lang}-{theme}/{s['name']}.png" for lang in LANGS for theme in THEMES for s in screens)


def check(root: Path) -> list[str]:
    """Probleme der abgelegten Screenshots; leer = alles aktuell."""
    spec = root / SPEC
    out = root / OUT
    problems: list[str] = []
    screens = load_spec(spec)
    expected = set(expected_files(screens))
    present = {p.relative_to(out).as_posix() for p in out.glob("*/*.png")} if out.exists() else set()
    problems += [f"fehlt: {f}" for f in sorted(expected - present)]
    problems += [f"übrig (nicht in screens.json): {f}" for f in sorted(present - expected)]
    manifest = out / "manifest.json"
    if not manifest.exists():
        problems.append("manifest.json fehlt – gen_help_screens.py laufen lassen")
    elif json.loads(manifest.read_text(encoding="utf-8")).get("spec_sha256") != spec_hash(spec):
        problems.append("veraltet: screens.json wurde nach dem letzten Erzeugen geändert – gen_help_screens.py laufen lassen")
    names = [s["name"] for s in screens]
    problems += [f"Name doppelt: {n}" for n in sorted({n for n in names if names.count(n) > 1})]
    problems += [f"{s['name']}: keine Markierung" for s in screens if not s.get("markers")]
    return problems


def run_tests(root: Path, target: Path, build: bool) -> None:
    env = dict(os.environ, WLD_HELP_SCREENS_DIR=str(target), WLD_HELP_SCREENS_SPEC=str(root / SPEC))
    cmd = ["dotnet", "test", str(root / TEST_PROJECT), "-c", "Release", "--filter", TEST_FILTER]
    if not build:
        cmd.append("--no-build")
    subprocess.run(cmd, cwd=root, env=env, check=True)


def install(source: Path, root: Path) -> int:
    """Ersetzt die Sprach-/Varianten-Ordner vollständig (übrige Bilder verschwinden) und schreibt das Manifest."""
    out = root / OUT
    out.mkdir(parents=True, exist_ok=True)
    files = expected_files(load_spec(root / SPEC))
    missing = [f for f in files if not (source / f).exists()]
    if missing:
        raise SystemExit(f"Test hat nicht alle Bilder erzeugt: {missing[:5]}")
    for lang in LANGS:
        for theme in THEMES:
            shutil.rmtree(out / f"{lang}-{theme}", ignore_errors=True)
    for f in files:
        (out / f).parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source / f, out / f)
    manifest = {"spec_sha256": spec_hash(root / SPEC), "files": files}
    (out / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8", newline="\n")
    return len(files)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--check", action="store_true", help="nur prüfen, nichts erzeugen")
    parser.add_argument("--no-build", action="store_true", help="Testprojekt nicht neu bauen")
    args = parser.parse_args(argv)
    root = args.root.resolve()
    if not args.check:
        with tempfile.TemporaryDirectory(prefix="wl-help-screens-") as tmp:
            run_tests(root, Path(tmp), build=not args.no_build)
            install(Path(tmp), root)
    problems = check(root)
    for problem in problems:
        print(problem)
    if problems:
        print(f"HELP SCREENS FEHLER {len(problems)}")
        return 1
    print(f"HELP SCREENS OK {len(expected_files(load_spec(root / SPEC)))}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
