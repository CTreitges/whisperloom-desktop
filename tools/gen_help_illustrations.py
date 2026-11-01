"""Erzeugt die Illustrationen der Hilfe (je Abschnitt ein SVG, 320×240) nur aus Loom-Tokens und -Glyphen.

Aufruf: py -3 -X utf8 tools/gen_help_illustrations.py [--root <repo>] [--out <ordner>]
Standard-Ausgabe: src/WhisperLoom.App/Assets/Help/illustrations/<abschnitt>.svg
Letzte Zeile: "ILLUSTRATIONS OK <anzahl>".

Die Bilder enthalten keinen Text; die Bildbeschreibung steht je Sprache in ``HelpImg_<abschnitt>`` (Localization).
Flach und schematisch wie die Android-Bilder (res/drawable/ill_*.xml). Zweimal laufen = identische Dateien
(Test: tools/tests/test_help_illustrations.py).
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from help_art import Canvas, load_tokens  # noqa: E402

OUT = Path("src/WhisperLoom.App/Assets/Help/illustrations")


def start(c: Canvas) -> None:
    """Erste Schritte: Fenster mit drei Schritten, zwei erledigt."""
    c.window(56, 28, 208, 184)
    for i, done in enumerate((True, True, False)):
        y = 74 + i * 44
        if done:
            c.circle(88, y, 13, fill="Accent")
            c.line([(82, y), (87, y + 5), (95, y - 5)], "OnAccent", 3)
        else:
            c.circle(88, y, 13, fill="Surface", stroke="Accent", width=2.5)
        c.text_lines(112, y - 8, [120, 84], gap=12)
    c.badge(250, 196, 22, "Recognition")


def dictation(c: Canvas) -> None:
    """Diktieren: Tasten halten, Mikrofon nimmt auf, Text erscheint im Fenster."""
    c.key(24, 172, 56, pressed=True)
    c.key(88, 172, 56, pressed=True)
    c.badge(84, 92, 32, "Recognition")
    c.wave(128, 92, [10, 22, 34, 18, 28, 12], step=8)
    c.arrow(182, 92, 196, 92)
    c.window(204, 40, 100, 132)
    c.text_lines(216, 76, [72, 60, 76, 40], gap=14)
    c.rect(258, 116, 2.5, 14, 1, fill="Accent")


def stages(c: Canvas) -> None:
    """Stufen: holpriger Rohtext → KI → sauberer Text; darunter die Stufen-Auswahl."""
    c.window(20, 28, 112, 120)
    c.text_lines(32, 62, [52, 18, 70], gap=14)
    c.text_lines(32, 104, [30, 44], gap=14)
    c.text_lines(90, 62, [26], token="Error")
    c.badge(160, 88, 20, "Text")
    c.window(188, 28, 112, 120)
    c.text_lines(200, 62, [88, 82, 88, 54], gap=14)
    for i in range(5):
        selected = i == 2
        c.rect(36 + i * 52, 176, 44, 26, 13, fill="Accent" if selected else "Surface", stroke=None if selected else "Separator", width=1.5)
        c.rect(48 + i * 52, 187, 20, 4, 2, fill="OnAccent" if selected else "Separator")


def selection(c: Canvas) -> None:
    """Markierung verarbeiten: markierte Zeile, kleiner Knopf, Stufen-Wähler."""
    c.window(20, 24, 200, 150)
    c.text_lines(36, 58, [150, 130], gap=14)
    c.rect(32, 82, 150, 18, 4, fill="Selection")
    c.text_lines(36, 88, [140], token="OnSelection")
    c.text_lines(36, 112, [160, 120, 90], gap=14)
    c.badge(198, 91, 13, "Text")
    c.rect(170, 112, 128, 104, 10, fill="Surface", stroke="Separator", width=1.5)
    for i in range(4):
        y = 124 + i * 22
        if i == 1:
            c.rect(176, y - 4, 116, 20, 6, fill="Selection")
        c.rect(184, y + 2, 10, 10, 3, fill="Accent")
        c.text_lines(202, y + 5, [60 - i * 6], token="OnSelection" if i == 1 else "Separator")


def history(c: Canvas) -> None:
    """Verlauf: Liste der letzten Diktate, ein Eintrag gewählt, daneben Kopieren."""
    c.window(28, 24, 196, 192)
    for i in range(4):
        y = 50 + i * 40
        if i == 1:
            c.rect(36, y, 180, 34, 8, fill="Selection")
        c.circle(54, y + 17, 9, stroke="Accent", width=2)
        c.line([(54, y + 12), (54, y + 17), (58, y + 20)], "Accent", 2)
        c.text_lines(72, y + 9, [120 - i * 10, 80], gap=10, token="OnSelection" if i == 1 else "Separator", height=4)
    c.rect(244, 92, 40, 48, 8, fill="Surface", stroke="Accent", width=2.5)
    c.rect(254, 82, 40, 48, 8, fill="Surface", stroke="Accent", width=2.5)
    c.text_lines(262, 96, [24, 18, 22], gap=9, height=4)


def output(c: Canvas) -> None:
    """Ausgabe: Ergebnis wird eingefügt; die Zwischenablage hält eine Kopie."""
    c.badge(52, 120, 26, "Text")
    c.arrow(84, 120, 118, 120)
    c.rect(124, 72, 72, 96, 10, fill="Surface", stroke="Separator", width=1.5)
    c.rect(144, 64, 32, 16, 5, fill="Accent")
    c.text_lines(136, 98, [48, 40, 48, 28], gap=13)
    c.arrow(202, 120, 228, 120)
    c.window(234, 52, 72, 136)
    c.text_lines(244, 88, [52, 44, 30], gap=14)
    c.rect(276, 114, 2.5, 14, 1, fill="Accent")


def recognition(c: Canvas) -> None:
    """Erkennung: Mikrofon → Wolke (online) oder Chip (lokal)."""
    c.badge(64, 120, 30, "Recognition")
    c.arrow(100, 108, 176, 70)
    c.arrow(100, 132, 176, 170)
    # Wolke aus Kreisen.
    for cx, cy, r in ((212, 66, 22), (236, 56, 26), (262, 68, 20)):
        c.circle(cx, cy, r, fill="Surface", stroke="Separator", width=1.5)
    c.rect(194, 66, 86, 22, 11, fill="Surface")
    c.wave(216, 72, [8, 14, 10, 16, 8], step=8)
    c.rect(196, 140, 72, 64, 12, fill="Managed")
    c.icon("LocalModels", 208, 148, 48, "OnManaged")


def providers(c: Canvas) -> None:
    """Eigene Anbieter und lokale Modelle: Server-Stapel und Chip, an die App angeschlossen."""
    c.window(118, 72, 96, 96)
    c.badge(166, 128, 18, "Recognition")
    c.rect(22, 40, 72, 72, 14, fill="Accent")
    c.icon("Providers", 34, 52, 48, "OnAccent")
    c.rect(22, 140, 72, 72, 14, fill="Managed")
    c.icon("LocalModels", 34, 152, 48, "OnManaged")
    c.line([(94, 76), (106, 76), (106, 110), (118, 110)], "Accent", 2.5)
    c.line([(94, 176), (106, 176), (106, 140), (118, 140)], "Accent", 2.5)
    c.rect(238, 44, 64, 150, 10, fill="Surface", stroke="Separator", width=1.5)
    for i in range(4):
        y = 60 + i * 34
        c.circle(254, y + 6, 6, fill="Accent" if i < 3 else "Separator")
        c.text_lines(266, y + 3, [28], gap=0)
    c.arrow(214, 120, 234, 120)


def company(c: Canvas) -> None:
    """Firma verbinden: Firmengebäude, Firmencode, Gerät mit Verwaltet-Schloss."""
    c.rect(28, 56, 72, 140, 8, fill="Surface", stroke="Separator", width=1.5)
    for row in range(5):
        for col in range(3):
            c.rect(40 + col * 20, 70 + row * 22, 10, 12, 2, fill="Accent" if (row + col) % 3 else "Separator")
    c.rect(54, 176, 20, 20, 3, fill="Separator")
    for i in range(4):
        c.rect(118 + i * 24, 112, 20, 16, 4, fill="Surface", stroke="Accent", width=2)
    c.arrow(122, 148, 206, 148)
    c.window(212, 70, 92, 112)
    c.text_lines(224, 106, [64, 48], gap=14)
    c.rect(234, 136, 48, 30, 8, fill="Managed")
    c.icon("Managed", 248, 140, 22, "OnManaged")


def privacy(c: Canvas) -> None:
    """Datenschutz: Schild mit Schloss, daneben ein maskierter Key."""
    c.path("M96,40 L150,56 V104 C150,146 128,172 96,188 C64,172 42,146 42,104 V56 Z", fill="Selection", stroke="Accent", width=3)
    c.icon("Managed", 74, 88, 44, "OnSelection")
    c.window(176, 64, 128, 112)
    c.rect(188, 104, 104, 26, 6, fill="SurfaceHigh", stroke="Separator", width=1.5)
    for i in range(6):
        c.circle(202 + i * 14, 117, 4, fill="TextSecondary")
    c.text_lines(188, 146, [80, 56], gap=12)


def hotkeys(c: Canvas) -> None:
    """Tastenkürzel: Tastatur, eine Kombination aus drei Tasten gedrückt."""
    c.rect(20, 64, 280, 136, 14, fill="Surface", stroke="Separator", width=1.5)
    pressed = {(0, 3), (3, 0), (3, 1)}
    widths = [(11, 22), (10, 22), (9, 22)]
    for row, (count, w) in enumerate(widths):
        for col in range(count):
            c.key(34 + row * 10 + col * 24.5, 76 + row * 30, w, 22, pressed=(row, col) in pressed)
    c.key(34, 166, 30, 22, pressed=True)
    c.key(70, 166, 30, 22, pressed=True)
    c.key(108, 166, 116, 22)
    c.key(232, 166, 54, 22)
    c.badge(160, 36, 20, "Recognition")


def trouble(c: Canvas) -> None:
    """Fehlerbehebung: Warnung → Lösung (Haken)."""
    c.window(24, 44, 124, 152)
    c.icon("Warning", 58, 80, 56, "Warning")
    c.text_lines(46, 150, [80, 56], gap=12)
    c.arrow(156, 120, 188, 120)
    c.window(196, 44, 100, 152)
    c.circle(246, 106, 26, fill="Accent")
    c.line([(234, 106), (243, 115), (259, 97)], "OnAccent", 4)
    c.text_lines(214, 150, [64, 44], gap=12)


def network(c: Canvas) -> None:
    """Netzwerk: App → Proxy → Internet."""
    c.window(18, 76, 84, 88)
    c.badge(60, 128, 16, "Recognition")
    c.arrow(106, 120, 132, 120)
    c.rect(138, 92, 52, 56, 10, fill="Managed")
    c.rect(150, 104, 28, 6, 3, fill="OnManaged")
    c.rect(150, 116, 28, 6, 3, fill="OnManaged")
    c.rect(150, 128, 18, 6, 3, fill="OnManaged")
    c.arrow(194, 120, 220, 120)
    c.circle(262, 120, 38, fill="Surface", stroke="Separator", width=1.5)
    c.icon("Network", 230, 88, 64, "Accent")


def it(c: Canvas) -> None:
    """Für die IT: Firmen-Server bzw. Vorgabedatei verteilt Einstellungen an mehrere Geräte."""
    c.rect(24, 40, 70, 70, 14, fill="Accent")
    c.icon("Providers", 35, 51, 48, "OnAccent")
    c.rect(32, 132, 54, 70, 6, fill="Surface", stroke="Separator", width=1.5)
    c.text_lines(42, 148, [34, 26, 34, 20], gap=11, height=4)
    for i in range(3):
        y = 30 + i * 66
        c.window(196, y, 100, 54)
        c.rect(262, y + 30, 22, 16, 5, fill="Managed")
        c.icon("Managed", 266, y + 31, 14, "OnManaged")
        c.text_lines(206, y + 32, [44], gap=0)
        c.line([(110, 120), (150, 120), (150, y + 27), (190, y + 27)], "Accent", 2.5)
    c.line([(94, 75), (110, 75), (110, 167), (86, 167)], "Accent", 2.5)


SCENES = {f.__name__: f for f in (start, dictation, stages, selection, history, output, recognition, providers, company, privacy,
                                  hotkeys, trouble, network, it)}


def render(name: str, colors, icons) -> str:
    canvas = Canvas(colors, icons)
    canvas.backdrop()
    SCENES[name](canvas)
    return canvas.svg()


def generate(root: Path, out: Path) -> list[Path]:
    colors, icons = load_tokens(root)
    out.mkdir(parents=True, exist_ok=True)
    written = []
    for name in SCENES:
        path = out / f"{name}.svg"
        # newline="\n": gleiche Bytes unter Windows und Linux.
        path.write_text(render(name, colors, icons), encoding="utf-8", newline="\n")
        written.append(path)
    return written


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument("--out", type=Path, help="Zielordner (Standard: Assets/Help/illustrations im Repo)")
    args = parser.parse_args(argv)
    written = generate(args.root, args.out or args.root / OUT)
    print(f"ILLUSTRATIONS OK {len(written)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
