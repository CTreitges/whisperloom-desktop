"""Test-Parität Kotlin → C#: prüft, dass jede Kotlin-Testklasse aus den Mapping-Dateien portiert
oder begründet ausgenommen ist.

Aufruf:
    py -3 -X utf8 tools/test_parity.py --android <android-repo> --mapping docs/test-mapping-contracts.md [--mapping …]

Mapping-Datei (Markdown-Tabelle, weitere Zeilen und Text werden ignoriert):
    | Kotlin-Klasse | C#-Klasse | Status | Begründung |
    |---|---|---|---|
    | RefinementTest | RefinementTest | portiert | |
    | api/HttpTest | — | ausgenommen | HttpURLConnection-spezifisch |

- Kotlin-Klasse: Klassenname oder Pfad-Suffix ohne .kt (``api/RefinePromptTest``), gesucht unter
  ``<android>/app/src/test*/``. Mehrdeutige Namen sind ein Fehler.
- portiert: die C#-Klasse (Name oder Namespace-qualifiziert) unter ``tests/`` hat mindestens so viele Fälle
  ([Fact]/[Theory]/[AvaloniaFact]/[AvaloniaTheory]) wie die Kotlin-Klasse @Test-Methoden.
- ausgenommen: Begründung ist Pflicht.

Ausgabe: Tabelle ``Kotlin-Klasse | @Test | C#-Klasse | Fälle``, letzte Zeile ``TEST-PARITY OK …`` (Exit 0)
oder ``TEST-PARITY FAIL …`` (Exit 1).
"""

from __future__ import annotations

import argparse
import re
import sys
from dataclasses import dataclass
from pathlib import Path

STATUS_PORTED = "portiert"
STATUS_EXCLUDED = "ausgenommen"
HEADER = ("kotlin-klasse", "c#-klasse", "status", "begründung")

KOTLIN_TEST = re.compile(r"@Test\b")
CSHARP_CASE = re.compile(r"\[(?:Avalonia)?(?:Fact|Theory)\b")
CSHARP_CLASS = re.compile(r"\bclass\s+(\w+)")
CSHARP_NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)", re.MULTILINE)


@dataclass(frozen=True)
class Row:
    source: str
    kotlin: str
    csharp: str
    status: str
    reason: str


def parse_mapping(path: Path) -> tuple[list[Row], list[str]]:
    """Liest die Mapping-Tabelle. Liefert Zeilen und Fehler (fehlende Tabelle, unbekannter Status)."""
    rows: list[Row] = []
    errors: list[str] = []
    in_table = False
    for line in path.read_text(encoding="utf-8").splitlines():
        cells = [c.strip() for c in line.strip().strip("|").split("|")] if line.strip().startswith("|") else None
        if cells is None:
            in_table = False
            continue
        if tuple(c.lower() for c in cells[:4]) == HEADER:
            in_table = True
            continue
        if not in_table or set("".join(cells)) <= set("-: "):
            continue
        cells += [""] * (4 - len(cells))
        kotlin, csharp, status, reason = cells[:4]
        if status not in (STATUS_PORTED, STATUS_EXCLUDED):
            errors.append(f"{path.name}: {kotlin}: unbekannter Status '{status}'")
            continue
        rows.append(Row(path.name, kotlin, csharp, status, reason))
    if not rows and not errors:
        errors.append(f"{path.name}: keine Tabelle mit Kopf 'Kotlin-Klasse | C#-Klasse | Status | Begründung'")
    return rows, errors


def find_kotlin(android: Path, name: str) -> list[Path]:
    suffix = "/" + name.strip("/") + ".kt"
    roots = [p for p in (android / "app" / "src").glob("test*") if p.is_dir()]
    return sorted(p for root in roots for p in root.rglob("*.kt") if p.as_posix().endswith(suffix))


def count_kotlin_tests(path: Path) -> int:
    return len(KOTLIN_TEST.findall(path.read_text(encoding="utf-8")))


def index_csharp(tests_dir: Path) -> dict[str, list[tuple[str, int]]]:
    """Klassenname → [(voll qualifizierter Name, Fälle)]. Annahme: eine Testklasse je Datei."""
    index: dict[str, list[tuple[str, int]]] = {}
    for path in sorted(tests_dir.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8")
        cases = len(CSHARP_CASE.findall(text))
        match = CSHARP_CLASS.search(text)
        if not match or cases == 0:
            continue
        ns = CSHARP_NAMESPACE.search(text)
        full = f"{ns.group(1)}.{match.group(1)}" if ns else match.group(1)
        index.setdefault(match.group(1), []).append((full, cases))
    return index


def resolve_csharp(index: dict[str, list[tuple[str, int]]], name: str) -> list[tuple[str, int]]:
    simple = name.rsplit(".", 1)[-1]
    return [c for c in index.get(simple, []) if "." not in name or c[0] == name]


def check(android: Path, mappings: list[Path], tests_dir: Path) -> tuple[list[str], list[str]]:
    """Liefert (Tabellenzeilen, Fehler)."""
    table = ["| Kotlin-Klasse | @Test | C#-Klasse | Fälle |", "|---|---|---|---|"]
    errors: list[str] = []
    rows: list[Row] = []
    for mapping in mappings:
        if not mapping.is_file():
            errors.append(f"Mapping-Datei fehlt: {mapping}")
            continue
        parsed, parse_errors = parse_mapping(mapping)
        rows += parsed
        errors += parse_errors

    seen: dict[str, str] = {}
    index = index_csharp(tests_dir)
    for row in rows:
        if row.kotlin in seen:
            errors.append(f"{row.kotlin}: doppelt ({seen[row.kotlin]}, {row.source})")
            continue
        seen[row.kotlin] = row.source

        found = find_kotlin(android, row.kotlin)
        if len(found) != 1:
            what = "nicht gefunden" if not found else f"mehrdeutig: {', '.join(p.name for p in found)}"
            errors.append(f"{row.kotlin}: Kotlin-Klasse {what}")
            continue
        kotlin_tests = count_kotlin_tests(found[0])

        if row.status == STATUS_EXCLUDED:
            if not row.reason:
                errors.append(f"{row.kotlin}: ausgenommen ohne Begründung")
            table.append(f"| {row.kotlin} | {kotlin_tests} | — | ausgenommen: {row.reason} |")
            continue

        matches = resolve_csharp(index, row.csharp)
        if len(matches) != 1:
            what = "nicht gefunden (oder ohne Fälle)" if not matches else "mehrdeutig"
            errors.append(f"{row.kotlin}: C#-Klasse '{row.csharp}' {what}")
            continue
        full, cases = matches[0]
        table.append(f"| {row.kotlin} | {kotlin_tests} | {full} | {cases} |")
        if cases < kotlin_tests:
            errors.append(f"{row.kotlin}: {cases} C#-Fälle < {kotlin_tests} @Test")
    return table, errors


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Test-Parität Kotlin → C#")
    parser.add_argument("--android", required=True, type=Path, help="Wurzel des Android-Repos")
    parser.add_argument("--mapping", required=True, action="append", type=Path, help="Mapping-Datei (mehrfach)")
    parser.add_argument("--tests-dir", type=Path, default=Path(__file__).resolve().parent.parent / "tests",
                        help="C#-Testordner (Standard: <repo>/tests)")
    args = parser.parse_args(argv)

    if not (args.android / "app" / "src").is_dir():
        print(f"TEST-PARITY FAIL: kein Android-Repo unter {args.android}")
        return 1
    table, errors = check(args.android, args.mapping, args.tests_dir)
    print("\n".join(table))
    for error in errors:
        print(f"FEHLER: {error}")
    ported = sum(1 for line in table[2:] if "ausgenommen:" not in line)
    excluded = len(table) - 2 - ported
    if errors:
        print(f"TEST-PARITY FAIL errors={len(errors)}")
        return 1
    print(f"TEST-PARITY OK ported={ported} exceptions={excluded}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
