"""Erzeugt das Symbol-Set der App aus den SVG-Quellen: design/icons/*.svg → src/WhisperLoom.App/Design/Icons.axaml.

Aufruf:  py -3 -X utf8 tools/gen_icons.py [--root <repo>]
Prüfen:  py -3 -X utf8 tools/gen_icons.py --check     (nur prüfen, nichts schreiben)
Letzte Zeile: "ICONS OK <anzahl>" bzw. "ICONS FEHLER <grund>" (Exit 1).

Die Symbole sind Strich-Symbole (24-px-Raster, Strich 1,75, runde Enden, Farbe aus dem Text; zielstruktur.md §8). Avalonia
zeichnet sie mit ``ds:LoomIcon`` als Kontur einer Geometrie. Gefüllte Punkte (``<circle fill="currentColor">``) werden darum zu
konzentrischen Ringen derselben Strichbreite, die zusammen genau die gefüllte Scheibe decken. Schlüssel: ``Loom.Icon.<Name>``
nach ``NAMES`` (Dateiname deutsch wie im Entwurf, Schlüssel englisch wie der Code).
"""
from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

SOURCE = Path("design/icons")
TARGET = Path("src/WhisperLoom.App/Design/Icons.axaml")
STROKE = 1.75

NAMES = {
    "abspielen": "Play", "aktualisieren": "Refresh", "aufklappen": "Expand", "aufnahme": "Record", "ausgabe": "Output",
    "bearbeiten": "Edit", "buehne": "Presentation", "datei": "File", "datenschutz": "Privacy", "diktat": "Dictation",
    "einfuegen": "Paste", "einrichtung": "Setup", "empfohlen": "Recommended", "erkennung": "Recognition",
    "erneut-senden": "Resend", "extern": "External", "fehler": "Error", "firma": "Company", "haekchen": "Check",
    "herunterladen": "Download", "hilfe": "Help", "hinzufuegen": "Add", "info": "Info", "kopieren": "Copy",
    "loeschen": "Delete", "markierung": "Selection", "mehr": "More", "mikrofon": "Microphone", "nach-oben": "MoveUp",
    "nach-unten": "MoveDown", "neu-verarbeiten": "Reprocess", "ordner": "Folder", "pause": "Pause",
    "programm-waehlen": "PickApp", "rueckgaengig": "Undo", "schliessen": "Close", "senden": "Send", "sprache": "Language",
    "stopp": "Stop", "stufe": "TextStage", "suche": "Search", "system": "System", "tastenkuerzel": "Hotkeys",
    "update": "Update", "verarbeitung": "Processing", "verbindungen": "Connections", "verlauf": "History",
    "verwaltet": "Managed", "verwerfen": "Discard", "vorrang": "Priority", "warnung": "Warning", "weiter": "Forward",
    "zuklappen": "Collapse", "zurueck": "Back", "zwischenablage": "Clipboard",
}

_ARGS = {"m": 2, "l": 2, "h": 1, "v": 1, "c": 6, "s": 4, "q": 4, "t": 2, "a": 7, "z": 0}
_SVG = "{http://www.w3.org/2000/svg}"


def num(v: float) -> str:
    text = f"{v:.3f}".rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


_NUMBER = re.compile(r"[\s,]*([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)")
_FLAG = re.compile(r"[\s,]*([01])")
_SEGMENT = re.compile(r"([MmLlHhVvCcSsQqTtAaZz])([^MmLlHhVvCcSsQqTtAaZz]*)")


def _arguments(command: str, text: str, d: str) -> list[str]:
    """Zahlen eines Befehls; bei Bögen sind die Flags je eine Ziffer („a1 1 0 012 2“ = Flags 0 und 1, Ziel 2 2)."""
    arc = command.lower() == "a"
    values: list[str] = []
    pos = 0
    while text[pos:].strip(" \t\r\n,"):
        pattern = _FLAG if arc and len(values) % 7 in (3, 4) else _NUMBER
        match = pattern.match(text, pos)
        if not match:
            raise ValueError(f"unbekannte Zeichen in Pfad: {d!r}")
        values.append(match.group(1))
        pos = match.end()
    return values


def normalize_path(d: str) -> str:
    """SVG-Pfaddaten in eindeutig getrennte Zahlen (Avalonia liest „1.1.9“, „c-.8.4“ oder „a1 1 0 012 2“ sonst nicht sicher)."""
    if not d.strip():
        raise ValueError("leerer Pfad")
    if _SEGMENT.sub("", d).strip(" \t\r\n,"):
        raise ValueError(f"Zahl vor dem ersten Befehl oder unbekannte Zeichen: {d!r}")
    out: list[str] = []
    for command, text in _SEGMENT.findall(d):
        args = _arguments(command, text, d)
        size = _ARGS[command.lower()]
        out.append(command)
        if size == 0:
            if args:
                raise ValueError(f"Z mit Zahlen: {d!r}")
            continue
        if not args or len(args) % size:
            raise ValueError(f"{command} mit {len(args)} Zahlen: {d!r}")
        out.append(" ".join(num(float(a)) for a in args))
    return " ".join(out)


def ring(cx: float, cy: float, r: float) -> str:
    return f"M {num(cx - r)} {num(cy)} A {num(r)} {num(r)} 0 1 0 {num(cx + r)} {num(cy)} A {num(r)} {num(r)} 0 1 0 {num(cx - r)} {num(cy)} Z"


def disk(cx: float, cy: float, r: float) -> list[str]:
    """Gefüllte Scheibe als Ringe der Strichbreite: jeder Ring deckt [ρ − s/2, ρ + s/2]; innen bleibt ein Ring mit ρ ≤ s/2."""
    half = STROKE / 2
    if r <= half:
        raise ValueError(f"gefüllter Punkt r={r} ist nicht größer als der halbe Strich ({half}) und würde zu groß gezeichnet")
    rings, rho = [], r - half
    while rho > half:
        rings.append(ring(cx, cy, rho))
        rho -= STROKE * 0.9
    rings.append(ring(cx, cy, max(rho, 0.01)))
    return rings


def geometry(svg_text: str) -> str:
    root = ET.fromstring(svg_text)
    if root.get("viewBox") != "0 0 24 24":
        raise ValueError("viewBox ist nicht 0 0 24 24")
    parts: list[str] = []
    for el in root.iter():
        tag = el.tag.removeprefix(_SVG)
        if tag == "svg":
            continue
        if tag == "path":
            parts.append(normalize_path(el.get("d", "")))
        elif tag == "circle":
            cx, cy, r = (float(el.get(k, "0")) for k in ("cx", "cy", "r"))
            if el.get("fill") == "currentColor":
                parts.extend(disk(cx, cy, r))
            else:
                parts.append(ring(cx, cy, r))
        else:
            raise ValueError(f"Element <{tag}> wird nicht unterstützt")
    if not parts:
        raise ValueError("leeres Symbol")
    return " ".join(parts)


def render(root: Path) -> tuple[str, int]:
    files = sorted((root / SOURCE).glob("*.svg"))
    stems = {f.stem for f in files}
    if stems != set(NAMES):
        missing, extra = sorted(set(NAMES) - stems), sorted(stems - set(NAMES))
        raise ValueError(f"Quellen passen nicht zu NAMES: fehlt {missing}, übrig {extra}")
    lines = [
        '<ResourceDictionary xmlns="https://github.com/avaloniaui"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">',
        "  <!--",
        "    Erzeugt von tools/gen_icons.py aus design/icons/*.svg – nicht von Hand ändern.",
        "    Eigenes Symbol-Set (zielstruktur.md §8): 24-px-Raster, Strich 1,75, runde Enden; gezeichnet von ds:LoomIcon.",
        "  -->",
    ]
    for f in sorted(files, key=lambda p: NAMES[p.stem]):
        lines.append(f'  <StreamGeometry x:Key="Loom.Icon.{NAMES[f.stem]}">{geometry(f.read_text(encoding="utf-8"))}</StreamGeometry>')
    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n", len(files)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args(argv)
    try:
        text, count = render(args.root)
    except (ValueError, ET.ParseError) as error:
        print(f"ICONS FEHLER {error}")
        return 1
    target = args.root / TARGET
    if args.check:
        current = target.read_text(encoding="utf-8").replace("\r\n", "\n") if target.exists() else ""
        if current != text:
            print(f"ICONS FEHLER {TARGET.as_posix()} veraltet – tools/gen_icons.py ausführen")
            return 1
    else:
        target.write_text(text, encoding="utf-8", newline="\n")
    print(f"ICONS OK {count}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
