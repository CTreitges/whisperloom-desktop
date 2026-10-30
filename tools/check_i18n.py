"""Prüft die UI-Texte der App: jeder Schlüssel in DE und EN, keiner leer, jeder im Code benutzte vorhanden.

Aufruf: py -3 -X utf8 tools/check_i18n.py [--root <repo>]
Letzte Zeile: "I18N OK keys=<n>" oder "I18N FEHLER <anzahl>" (Exit 1).

Benutzte Schlüssel:
- XAML: {Binding [Schlüssel], Source={x:Static l:Loc.Instance}}
- C#: Loc.Instance["Schlüssel"], loc["Schlüssel"] und Literale "Error_…", "Access_…", "Key_…"
- dynamisch zusammengesetzte Präfixe (Stufe, Zustand, Proxy): Liste DYNAMIC unten, gleich halten mit dem Code.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

APP = Path("src/WhisperLoom.App")
LANGS = ("de", "en")

# "Stage_" + RefineMode.Key(), "State_" + DictationState, "Proxy_" + DesktopSettings.ProxyMode.
DYNAMIC = (
    [f"Stage_{k}" for k in ("off", "polish", "beautify", "summarize", "readable", "prompt")]
    + [f"State_{k}" for k in ("idle", "recording", "paused", "processing")]
    + [f"Proxy_{k}" for k in ("system", "off", "manual")]
)

XAML_KEY = re.compile(r"\{Binding \[([A-Za-z]+_[A-Za-z0-9_]+)\], Source=\{x:Static l:Loc\.Instance\}\}")
CS_KEY = re.compile(r"(?:Loc\.Instance|loc)\[\"([A-Za-z]+_[A-Za-z0-9_]+)\"\]")
CS_LITERAL = re.compile(r"\"((?:Error|Access|Key)_[A-Za-z]+)\"")


def used_keys(app: Path) -> set[str]:
    keys = set(DYNAMIC)
    for path in app.rglob("*.axaml"):
        keys.update(XAML_KEY.findall(path.read_text(encoding="utf-8")))
    for path in app.rglob("*.cs"):
        if "obj" in path.parts or "bin" in path.parts:
            continue
        text = path.read_text(encoding="utf-8")
        keys.update(CS_KEY.findall(text))
        keys.update(CS_LITERAL.findall(text))
    return keys


def check(root: Path) -> tuple[list[str], int]:
    app = root / APP
    tables = {lang: json.loads((app / "Localization" / f"{lang}.json").read_text(encoding="utf-8")) for lang in LANGS}
    errors: list[str] = []
    de, en = set(tables["de"]), set(tables["en"])
    errors += [f"nur DE: {k}" for k in sorted(de - en)]
    errors += [f"nur EN: {k}" for k in sorted(en - de)]
    for lang, table in tables.items():
        errors += [f"leer ({lang}): {k}" for k, v in sorted(table.items()) if not str(v).strip()]
    errors += [f"fehlt: {k}" for k in sorted(used_keys(app) - (de & en))]
    return errors, len(de | en)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    args = parser.parse_args(argv)
    errors, count = check(args.root)
    for e in errors:
        print(e)
    if errors:
        print(f"I18N FEHLER {len(errors)}")
        return 1
    print(f"I18N OK keys={count}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
