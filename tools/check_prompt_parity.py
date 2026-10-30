"""Prompt-Parität Kotlin → C#: jedes Prompt-Fragment aus ``api/RefinePrompt.kt`` steht normalisiert in genau einer
C#-Ressource (``src/WhisperLoom.Core/Prompts/<de|en>/*.txt``), und umgekehrt.

Aufruf:
    py -3 -X utf8 tools/check_prompt_parity.py --android <android-repo> [--desktop <repo>] [--exceptions <md>]

Fragment = ein Stück Prompt-Text zwischen zwei Nähten, exakt verglichen (keine Ähnlichkeit):
- Kotlin: alle String-Literale der Datei außer denen in ``fun build``/``fun userText``, in ``Regex(…)`` und den
  ``const val TAG_*`` (Markierungen). Mit ``+`` verkettete Literale zählen als eines; ``$TAG_*`` wird durch den
  Wert ersetzt; ``.trimIndent()`` wie in Kotlin angewandt. Nähte sind Zeilenumbrüche, ``$NAME``-Verweise
  (``$FRAME``) und ``${…}``-Ausdrücke; die String-Literale in einem Ausdruck (``if (p) "A" else "B"``) sind
  eigene Fragmente.
- C#: Ressourcentext mit LF, ohne abschließenden Umbruch. Nähte sind Zeilenumbrüche und Vorlagen-Marken
  ``{{FRAME}}`` und ``{{p?A|B}}``/``{{f?A|B}}``; A und B sind eigene Fragmente.
- Fragmente nur aus Leerraum zählen nicht. Verglichen wird als Multimenge („Beispiele:“ steht in vier Stufen).

Ausnahmen: ``docs/parity-exceptions.md``, Abschnitt ``## Prompts``, Tabelle ``| Fragment | Seite | Begründung |``
mit Seite ``android`` (nur in Kotlin) oder ``desktop`` (nur in C#); Begründung ist Pflicht, ungenutzte
Ausnahmen sind ein Fehler.

Ausgabe: Abweichungen je Zeile, letzte Zeile ``PROMPT-PARITY OK prompts=<n> exceptions=<k>`` (Exit 0) oder
``PROMPT-PARITY FAIL …`` (Exit 1).
"""

from __future__ import annotations

import argparse
import re
import sys
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

KOTLIN_FILE = Path("app/src/main/java/com/chris/whisperloom/api/RefinePrompt.kt")
RESOURCE_DIR = Path("src/WhisperLoom.Core/Prompts")
SEAM = "\x00"
EXCLUDED_FUNS = ("build", "userText")
SUBSTITUTED = re.compile(r"const\s+val\s+(TAG_\w+)\s*=\s*$")
TEMPLATE_TOKEN = re.compile(r"\{\{(?:(FRAME)|([pf])\?([^|}]*)\|([^}]*))\}\}")
ESCAPES = {"n": "\n", "t": "\t", "r": "\r", "b": "\b", '"': '"', "'": "'", "\\": "\\", "$": "$"}


# --- Kotlin ---------------------------------------------------------------------------------------------------


@dataclass
class KString:
    """Ein String-Literal: Teile sind Text, ``("ref", name)`` oder ``("expr", [KString…])``."""

    start: int
    end: int
    raw: bool
    parts: list = field(default_factory=list)


class KotlinScanner:
    """Liest Strings und Kommentare einer Kotlin-Datei; ``mask`` ist der Quelltext ohne beides (gleiche Länge)."""

    def __init__(self, src: str) -> None:
        self.src = src
        self.mask = list(src)

    def scan(self, i: int = 0, until_brace: bool = False) -> tuple[list[KString], int]:
        src, nodes, depth = self.src, [], 0
        while i < len(src):
            if src.startswith("//", i):
                end = src.find("\n", i)
                end = len(src) if end < 0 else end
                self._blank(i, end)
                i = end
            elif src.startswith("/*", i):
                end = self._block_comment_end(i)
                self._blank(i, end)
                i = end
            elif src.startswith('"', i):
                node, i = self._string(i)
                nodes.append(node)
            elif src[i] == "'":
                end = i + 1
                while end < len(src) and src[end] != "'":
                    end += 2 if src[end] == "\\" else 1
                i = end + 1
            elif until_brace and src[i] == "{":
                depth += 1
                i += 1
            elif until_brace and src[i] == "}":
                if depth == 0:
                    return nodes, i + 1
                depth -= 1
                i += 1
            else:
                i += 1
        return nodes, i

    def _blank(self, start: int, end: int) -> None:
        for k in range(start, end):
            if self.mask[k] != "\n":
                self.mask[k] = " "

    def _block_comment_end(self, i: int) -> int:
        depth, i = 0, i
        while i < len(self.src):
            if self.src.startswith("/*", i):
                depth, i = depth + 1, i + 2
            elif self.src.startswith("*/", i):
                depth, i = depth - 1, i + 2
                if depth == 0:
                    return i
            else:
                i += 1
        return i

    def _string(self, start: int) -> tuple[KString, int]:
        src = self.src
        raw = src.startswith('"""', start)
        i = start + (3 if raw else 1)
        node, text = KString(start, start, raw), []

        def flush() -> None:
            if text:
                node.parts.append("".join(text))
                text.clear()

        while i < len(src):
            if raw and src.startswith('"""', i):
                i += 3
                break
            if not raw and src[i] == '"':
                i += 1
                break
            if not raw and src[i] == "\\":
                if src[i + 1] == "u":
                    text.append(chr(int(src[i + 2:i + 6], 16)))
                    i += 6
                else:
                    text.append(ESCAPES.get(src[i + 1], src[i + 1]))
                    i += 2
                continue
            if src[i] == "$" and src.startswith("${", i):
                flush()
                inner, i = self.scan(i + 2, until_brace=True)
                node.parts.append(("expr", inner))
                continue
            if src[i] == "$" and i + 1 < len(src) and (src[i + 1].isalpha() or src[i + 1] == "_"):
                flush()
                m = re.match(r"\w+", src[i + 1:])
                node.parts.append(("ref", m.group(0)))
                i += 1 + m.end()
                continue
            text.append(src[i])
            i += 1
        flush()
        node.end = i
        self._blank(start, i)
        return node, i


def _excluded_spans(mask: str) -> list[tuple[int, int]]:
    """Rümpfe von ``fun build``/``fun userText`` (im Quelltext ohne Strings und Kommentare): ``{ … }`` oder
    ein Ausdrucksrumpf ``= …`` bis zum Zeilenende."""
    spans = []
    for m in re.finditer(r"\bfun\s+(\w+)\s*\(", mask):
        if m.group(1) not in EXCLUDED_FUNS:
            continue
        depth, i = 1, m.end()
        while depth:
            depth += {"(": 1, ")": -1}.get(mask[i], 0)
            i += 1
        body = re.compile(r"[{=]").search(mask, i).start()
        if mask[body] == "=":
            end = mask.find("\n", body)
            spans.append((m.start(), len(mask) if end < 0 else end))
            continue
        depth, j = 0, body
        while True:
            depth += {"{": 1, "}": -1}.get(mask[j], 0)
            j += 1
            if depth == 0:
                break
        spans.append((m.start(), j))
    return spans


def _flatten(node: KString, subst: dict[str, str], nested: list[KString]) -> str:
    out = []
    for part in node.parts:
        if isinstance(part, str):
            out.append(part)
        elif part[0] == "ref":
            out.append(subst.get(part[1], SEAM))
        else:
            out.append(SEAM)
            nested.extend(part[1])
    return "".join(out)


def _trim_indent(text: str) -> str:
    """Kotlins ``String.trimIndent()`` (Nähte zählen als Nicht-Leerraum)."""
    lines = text.split("\n")
    indents = [len(l) - len(l.lstrip()) for l in lines if l.strip()]
    cut = min(indents, default=0)
    last = len(lines) - 1
    kept = [l[cut:] for k, l in enumerate(lines) if not ((k == 0 or k == last) and not l.strip())]
    return "\n".join(kept)


def _segments(text: str) -> list[str]:
    return [s for s in re.split(f"[\n{SEAM}]", text) if s.strip()]


def kotlin_fragments(src: str) -> list[str]:
    src = src.replace("\r\n", "\n")
    scanner = KotlinScanner(src)
    nodes, _ = scanner.scan()
    mask = "".join(scanner.mask)
    excluded = _excluded_spans(mask)

    subst, kept = {}, []
    for node in nodes:
        before = mask[:node.start]
        if m := SUBSTITUTED.search(before):
            subst[m.group(1)] = "".join(p for p in node.parts if isinstance(p, str))
        elif re.search(r"Regex\(\s*$", before) or any(a <= node.start < b for a, b in excluded):
            continue
        else:
            kept.append(node)

    # "a" + "b" ist ein Literal: aufeinanderfolgende Knoten nur mit "+" dazwischen zusammenfassen.
    groups: list[list[KString]] = []
    for node in kept:
        if groups and re.fullmatch(r"\s*\+\s*", mask[groups[-1][-1].end:node.start]):
            groups[-1].append(node)
        else:
            groups.append([node])

    fragments: list[str] = []
    pending = groups
    while pending:
        nested: list[KString] = []
        for group in pending:
            text = "".join(_flatten(n, subst, nested) for n in group)
            last = group[-1]
            if last.raw and re.match(r"\s*\.trimIndent\(\)", mask[last.end:]):
                text = _trim_indent(text)
            fragments += _segments(text)
        pending = [[n] for n in nested]
    return fragments


# --- C# --------------------------------------------------------------------------------------------------------


def resource_fragments(text: str) -> list[str]:
    text = text.replace("\r\n", "\n").rstrip("\n")
    fragments: list[str] = []

    def seam(m: re.Match[str]) -> str:
        if m.group(2):
            fragments.extend(_segments(m.group(3)) + _segments(m.group(4)))
        return SEAM

    return _segments(TEMPLATE_TOKEN.sub(seam, text)) + fragments


def desktop_fragments(resource_dir: Path) -> dict[str, list[str]]:
    return {
        p.relative_to(resource_dir).as_posix(): resource_fragments(p.read_text(encoding="utf-8"))
        for p in sorted(resource_dir.rglob("*.txt"))
    }


# --- Ausnahmen und Vergleich -----------------------------------------------------------------------------------


def parse_exceptions(path: Path) -> tuple[list[tuple[str, str, str]], list[str]]:
    """Zeilen (Fragment, Seite, Begründung) aus dem Abschnitt ``## Prompts``."""
    if not path.is_file():
        return [], []
    rows, errors, in_section = [], [], False
    for line in path.read_text(encoding="utf-8").splitlines():
        if line.startswith("## "):
            in_section = line.strip() == "## Prompts"
            continue
        if not in_section or not line.strip().startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 3 or cells[0].lower() == "fragment" or set("".join(cells)) <= set("-: "):
            continue
        fragment, side, reason = cells[0].strip("`"), cells[1], cells[2]
        if side not in ("android", "desktop"):
            errors.append(f"Ausnahme '{fragment[:60]}': Seite '{side}' ist nicht android|desktop")
        elif not reason:
            errors.append(f"Ausnahme '{fragment[:60]}': Begründung fehlt")
        else:
            rows.append((fragment, side, reason))
    return rows, errors


def check(kotlin: list[str], desktop: dict[str, list[str]], exceptions: list[tuple[str, str, str]]) -> list[str]:
    errors = []
    want = Counter(kotlin)
    have = Counter(f for frags in desktop.values() for f in frags)
    missing, extra = want - have, have - want
    used = set()
    for fragment, side, _ in exceptions:
        pool = missing if side == "android" else extra
        if pool[fragment] > 0:
            pool[fragment] -= 1
            used.add((fragment, side))
        else:
            errors.append(f"Ausnahme ungenutzt ({side}): {fragment[:80]}")
    where: dict[str, list[str]] = {}
    for name, frags in desktop.items():
        for f in dict.fromkeys(frags):
            where.setdefault(f, []).append(name)
    for fragment, n in (+missing).items():
        errors.append(f"fehlt in C# ({n}×): {fragment[:100]}")
    for fragment, n in (+extra).items():
        errors.append(f"nur in C# ({n}×, {", ".join(where[fragment])}): {fragment[:100]}")
    return errors


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--android", type=Path, required=True)
    ap.add_argument("--desktop", type=Path, default=Path(__file__).resolve().parents[1])
    ap.add_argument("--exceptions", type=Path)
    args = ap.parse_args(argv)
    exceptions_path = args.exceptions or args.desktop / "docs" / "parity-exceptions.md"

    kotlin_path = args.android / KOTLIN_FILE
    resource_dir = args.desktop / RESOURCE_DIR
    errors: list[str] = []
    if not kotlin_path.is_file():
        errors.append(f"Kotlin-Datei fehlt: {kotlin_path}")
    if not resource_dir.is_dir():
        errors.append(f"Ressourcen fehlen: {resource_dir}")
    exceptions, exc_errors = parse_exceptions(exceptions_path)
    errors += exc_errors
    if not errors:
        kotlin = kotlin_fragments(kotlin_path.read_text(encoding="utf-8"))
        desktop = desktop_fragments(resource_dir)
        print(f"Kotlin: {len(kotlin)} Fragmente aus {KOTLIN_FILE.as_posix()}")
        print(f"C#: {sum(len(f) for f in desktop.values())} Fragmente aus {len(desktop)} Ressourcen")
        errors += check(kotlin, desktop, exceptions)
    for e in errors:
        print(e)
    if errors:
        print(f"PROMPT-PARITY FAIL errors={len(errors)}")
        return 1
    print(f"PROMPT-PARITY OK prompts={len(kotlin)} exceptions={len(exceptions)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
