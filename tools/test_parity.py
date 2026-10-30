"""Test-Parität Kotlin → C#: prüft, dass jede Kotlin-Testklasse aus den Mapping-Dateien portiert
oder begründet ausgenommen ist — und mit ``--require-file``, dass keine Soll-Klasse fehlt.

Aufruf:
    py -3 -X utf8 tools/test_parity.py --android <android-repo> --mapping docs/test-mapping-contracts.md \
        [--mapping …] [--require-file docs/test-parity-required.txt]

Mapping-Datei (Markdown-Tabelle, weitere Zeilen und Text werden ignoriert):
    | Kotlin-Klasse | C#-Klasse | Status | Begründung |
    |---|---|---|---|
    | RefinementTest | RefinementTest | portiert | |
    | TranscriptionEngineTest | EngineRunTest, EngineRefineTest | portiert | |
    | api/HttpTest | — | ausgenommen | HttpURLConnection-spezifisch |

- Kotlin-Klasse: Klassenname, optional mit Paket-Pfad davor (``api/RefinePromptTest``), gesucht als
  ``class <Name>`` in ``<android>/app/src/test*/``. Mehrere Klassen je Datei sind erlaubt; mehrdeutige
  Namen sind ein Fehler.
- C#-Klasse: ein oder mehrere Namen (kommagetrennt, einfach oder Namespace-qualifiziert) unter ``tests/``;
  ``partial``-Teile über mehrere Dateien zählen zusammen. Fälle = [Fact]/[Theory] (auch Avalonia-, mit
  weiteren Attributen davor), ohne ``Skip`` und ohne auskommentierte.
- portiert: Summe der C#-Fälle ≥ Anzahl Kotlin-@Test. ausgenommen: Begründung ist Pflicht.

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

# Strings und Kommentare in einem Durchgang (links nach rechts), damit "//" in URLs kein Kommentar ist
# und Klammern in Strings die Klassengrenzen nicht verschieben.
STRINGS_AND_COMMENTS = re.compile(
    r'"""[\s\S]*?"""|@"(?:""|[^"])*"|"(?:\\.|[^"\\\n])*"|\'(?:\\.|[^\'\\\n])\'|//[^\n]*|/\*[\s\S]*?\*/'
)
CLASS_DECL = re.compile(r"\bclass\s+(\w+)")
KOTLIN_CASE = re.compile(r"@Test\b")
CSHARP_CASE = re.compile(
    r"\[(?:[^\[\]]*,\s*)?(?:Xunit\.)?(?:Avalonia)?(?:Fact|Theory)(?:Attribute)?\b(?!\s*\([^)\]]*\bSkip\s*=)"
)
CSHARP_NAMESPACE = re.compile(r"^\s*namespace\s+([\w.]+)", re.MULTILINE)


@dataclass(frozen=True)
class Row:
    source: str
    kotlin: str
    csharp: str
    status: str
    reason: str


def strip_code(text: str) -> str:
    """Ersetzt Strings durch "" und Kommentare durch ein Leerzeichen (Zeilenumbrüche bleiben)."""
    def repl(m: re.Match[str]) -> str:
        s = m.group(0)
        if s.startswith(("//", "/*")):
            return " " + "\n" * s.count("\n")
        return '""' + "\n" * s.count("\n")
    return STRINGS_AND_COMMENTS.sub(repl, text)


def class_cases(text: str, case_re: re.Pattern[str]) -> dict[str, int]:
    """Klassenname → Fälle in seinem Rumpf (ohne die Rümpfe verschachtelter Klassen)."""
    code = strip_code(text)
    bodies: list[tuple[str, int, int]] = []
    decls = list(CLASS_DECL.finditer(code))
    for i, decl in enumerate(decls):
        limit = decls[i + 1].start() if i + 1 < len(decls) else len(code)
        start = _body_start(code, decl.end(), limit)
        if start is not None:
            bodies.append((decl.group(1), start, _body_end(code, start)))
    counts = {name: 0 for name, _, _ in bodies}
    for case in case_re.finditer(code):
        inner = [b for b in bodies if b[1] < case.start() < b[2]]
        if inner:
            counts[max(inner, key=lambda b: b[1])[0]] += 1
    return counts


def _body_start(code: str, pos: int, limit: int) -> int | None:
    """Erste "{" auf Klammertiefe 0 nach der Deklaration, vor der nächsten Klasse; sonst kein Rumpf
    (Kotlin ``data class P(val a: Int)``, C# ``record R(int A);``)."""
    depth = 0
    for i in range(pos, limit):
        c = code[i]
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        elif c == "{" and depth <= 0:
            return i
        elif c in ";}" and depth <= 0:
            return None
    return None


def _body_end(code: str, start: int) -> int:
    depth = 0
    for i in range(start, len(code)):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return i
    return len(code)


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


def index_kotlin(android: Path) -> dict[str, list[tuple[Path, int]]]:
    """Klassenname → [(Datei, @Test-Anzahl)] über alle Testquellen."""
    index: dict[str, list[tuple[Path, int]]] = {}
    roots = [p for p in (android / "app" / "src").glob("test*") if p.is_dir()]
    for path in sorted(p for root in roots for p in root.rglob("*.kt")):
        for name, cases in class_cases(path.read_text(encoding="utf-8"), KOTLIN_CASE).items():
            index.setdefault(name, []).append((path, cases))
    return index


def resolve_kotlin(index: dict[str, list[tuple[Path, int]]], key: str) -> list[tuple[Path, int]]:
    prefix, _, name = key.strip("/").rpartition("/")
    return [e for e in index.get(name, []) if not prefix or e[0].parent.as_posix().endswith("/" + prefix)]


def index_csharp(tests_dir: Path) -> dict[str, dict[str, int]]:
    """Klassenname → {voll qualifizierter Name: Fälle}, partial-Teile summiert; nur Klassen mit Fällen."""
    index: dict[str, dict[str, int]] = {}
    for path in sorted(tests_dir.rglob("*.cs")):
        if any(part in ("bin", "obj") for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8")
        ns = CSHARP_NAMESPACE.search(strip_code(text))
        for name, cases in class_cases(text, CSHARP_CASE).items():
            if cases:
                full = f"{ns.group(1)}.{name}" if ns else name
                bucket = index.setdefault(name, {})
                bucket[full] = bucket.get(full, 0) + cases
    return index


def resolve_csharp(index: dict[str, dict[str, int]], name: str) -> list[tuple[str, int]]:
    simple = name.rsplit(".", 1)[-1]
    return [(full, n) for full, n in index.get(simple, {}).items() if "." not in name or full == name]


def check(android: Path, mappings: list[Path], tests_dir: Path, required: list[str]) -> tuple[list[str], list[str]]:
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

    kotlin_index = index_kotlin(android)
    csharp_index = index_csharp(tests_dir)
    seen: dict[tuple[Path, str], str] = {}
    for row in rows:
        found = resolve_kotlin(kotlin_index, row.kotlin)
        if len(found) != 1:
            what = "nicht gefunden" if not found else f"mehrdeutig: {', '.join(str(p) for p, _ in found)}"
            errors.append(f"{row.kotlin}: Kotlin-Klasse {what}")
            continue
        identity = (found[0][0], row.kotlin.rpartition("/")[2])
        if identity in seen:
            errors.append(f"{row.kotlin}: doppelt ({seen[identity]}, {row.source})")
            continue
        seen[identity] = row.source
        kotlin_tests = found[0][1]

        if row.status == STATUS_EXCLUDED:
            if not row.reason:
                errors.append(f"{row.kotlin}: ausgenommen ohne Begründung")
            table.append(f"| {row.kotlin} | {kotlin_tests} | — | ausgenommen: {row.reason} |")
            continue

        names = [n.strip() for n in row.csharp.split(",") if n.strip()]
        resolved: list[tuple[str, int]] = []
        for name in names or [""]:
            matches = resolve_csharp(csharp_index, name)
            if len(matches) != 1:
                what = "nicht gefunden (oder ohne Fälle)" if not matches else "mehrdeutig"
                errors.append(f"{row.kotlin}: C#-Klasse '{name}' {what}")
            else:
                resolved.append(matches[0])
        if len(resolved) != len(names) or not names:
            continue
        cases = sum(n for _, n in resolved)
        table.append(f"| {row.kotlin} | {kotlin_tests} | {', '.join(f for f, _ in resolved)} | {cases} |")
        if cases < kotlin_tests:
            errors.append(f"{row.kotlin}: {cases} C#-Fälle < {kotlin_tests} @Test")

    for key in required:
        found = resolve_kotlin(kotlin_index, key)
        if len(found) != 1:
            errors.append(f"Soll-Klasse {key}: Kotlin-Klasse {'nicht gefunden' if not found else 'mehrdeutig'}")
        elif (found[0][0], key.rpartition("/")[2]) not in seen:
            errors.append(f"Soll-Klasse {key}: fehlt in allen Mapping-Dateien")
    return table, errors


def read_required(path: Path) -> list[str]:
    lines = (line.split("#", 1)[0].strip() for line in path.read_text(encoding="utf-8").splitlines())
    return [line for line in lines if line]


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Test-Parität Kotlin → C#")
    parser.add_argument("--android", required=True, type=Path, help="Wurzel des Android-Repos")
    parser.add_argument("--mapping", required=True, action="append", type=Path, help="Mapping-Datei (mehrfach)")
    parser.add_argument("--require-file", type=Path, help="Soll-Liste: je Zeile eine Kotlin-Klasse, # = Kommentar")
    parser.add_argument("--tests-dir", type=Path, default=Path(__file__).resolve().parent.parent / "tests",
                        help="C#-Testordner (Standard: <repo>/tests)")
    args = parser.parse_args(argv)

    if not (args.android / "app" / "src").is_dir():
        print(f"TEST-PARITY FAIL: kein Android-Repo unter {args.android}")
        return 1
    required = read_required(args.require_file) if args.require_file else []
    table, errors = check(args.android, args.mapping, args.tests_dir, required)
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
