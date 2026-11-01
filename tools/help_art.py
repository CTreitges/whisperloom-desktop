"""Bausteine für die Hilfe-Illustrationen: Loom-Tokens und Glyphen aus dem Designsystem, Formen als SVG-Pfade.

Jede Form ist ein ``<path>`` mit Füllung oder Strich in einer Token-Farbe. Die Datei trägt die Farbe der hellen Variante
(für Betrachter außerhalb der App) und den Token-Namen in ``data-fill`` bzw. ``data-stroke``; die App färbt danach je
Hell/Dunkel um (``Views/Help/HelpIllustration.cs``). Ausgabe ist rein textuell und ohne Zeitstempel, also deterministisch.

Doku: https://www.w3.org/TR/SVG2/paths.html (abgerufen 2026-11-01).
"""
from __future__ import annotations

import re
from pathlib import Path
from xml.etree import ElementTree

TOKENS_FILE = Path("src/WhisperLoom.App/Design/Tokens.axaml")
WIDTH, HEIGHT = 320, 240
_X = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"
_NS = "{https://github.com/avaloniaui}"


def load_tokens(root: Path) -> tuple[dict[str, dict[str, str]], dict[str, str]]:
    """Liest Farben (Token → {"light", "dark"}) und Glyphen (Name → Pfaddaten) aus ``Tokens.axaml``.

    Themen-Farben heißen wie ihr Pinsel ohne ``Loom.`` (z. B. ``Accent``), Kachel-Farben ``Tile.Teal`` usw. (gleich in
    beiden Varianten).
    """
    tree = ElementTree.parse(root / TOKENS_FILE)
    colors: dict[str, dict[str, str]] = {}
    for theme in tree.iter(f"{_NS}ResourceDictionary"):
        variant = theme.get(_X)
        if variant not in ("Light", "Dark"):
            continue
        for brush in theme.iter(f"{_NS}SolidColorBrush"):
            name = brush.get(_X).removeprefix("Loom.")
            colors.setdefault(name, {})[variant.lower()] = brush.get("Color").upper()
    icons: dict[str, str] = {}
    for element in tree.getroot():
        key = element.get(_X) or ""
        if element.tag == f"{_NS}SolidColorBrush" and key.startswith("Loom.Tile."):
            value = element.get("Color").upper()
            colors[key.removeprefix("Loom.")] = {"light": value, "dark": value}
        elif element.tag == f"{_NS}StreamGeometry" and key.startswith("Loom.Icon."):
            icons[key.removeprefix("Loom.Icon.")] = (element.text or "").strip()
    return colors, icons


def num(value: float) -> str:
    """Zahl kurz und stabil: höchstens zwei Nachkommastellen, ohne Nullen am Ende."""
    text = f"{round(value, 2):.2f}".rstrip("0").rstrip(".")
    return "0" if text in ("-0", "") else text


class Canvas:
    """Sammelt Pfade einer Illustration in Zeichenreihenfolge."""

    def __init__(self, colors: dict[str, dict[str, str]], icons: dict[str, str]):
        self._colors = colors
        self._icons = icons
        self._parts: list[str] = []

    def _color(self, token: str) -> str:
        if token not in self._colors:
            raise KeyError(f"Unbekanntes Loom-Token: {token}")
        return self._colors[token]["light"]

    def path(self, d: str, fill: str | None = None, stroke: str | None = None, width: float = 2, transform: str | None = None) -> None:
        attrs = [f'd="{d}"']
        if fill:
            attrs.append(f'fill="{self._color(fill)}" data-fill="{fill}"')
        else:
            attrs.append('fill="none"')
        if stroke:
            attrs.append(f'stroke="{self._color(stroke)}" data-stroke="{stroke}" stroke-width="{num(width)}" '
                         'stroke-linecap="round" stroke-linejoin="round"')
        if transform:
            attrs.append(f'transform="{transform}"')
        self._parts.append(f"  <path {' '.join(attrs)}/>")

    # Formen -----------------------------------------------------------------------------------------------------------
    def rect(self, x: float, y: float, w: float, h: float, r: float = 0, fill: str | None = None, stroke: str | None = None, width: float = 2) -> None:
        r = min(r, w / 2, h / 2)
        if r <= 0:
            d = f"M{num(x)},{num(y)} h{num(w)} v{num(h)} h{num(-w)} z"
        else:
            d = (f"M{num(x + r)},{num(y)} h{num(w - 2 * r)} a{num(r)},{num(r)} 0 0 1 {num(r)},{num(r)} v{num(h - 2 * r)} "
                 f"a{num(r)},{num(r)} 0 0 1 {num(-r)},{num(r)} h{num(-(w - 2 * r))} a{num(r)},{num(r)} 0 0 1 {num(-r)},{num(-r)} "
                 f"v{num(-(h - 2 * r))} a{num(r)},{num(r)} 0 0 1 {num(r)},{num(-r)} z")
        self.path(d, fill, stroke, width)

    def circle(self, cx: float, cy: float, r: float, fill: str | None = None, stroke: str | None = None, width: float = 2) -> None:
        d = f"M{num(cx - r)},{num(cy)} a{num(r)},{num(r)} 0 1 0 {num(2 * r)},0 a{num(r)},{num(r)} 0 1 0 {num(-2 * r)},0 z"
        self.path(d, fill, stroke, width)

    def line(self, points: list[tuple[float, float]], stroke: str, width: float = 3, closed: bool = False, fill: str | None = None) -> None:
        d = "M" + " L".join(f"{num(x)},{num(y)}" for x, y in points) + (" z" if closed else "")
        self.path(d, fill, stroke, width)

    def polygon(self, points: list[tuple[float, float]], fill: str) -> None:
        self.path("M" + " L".join(f"{num(x)},{num(y)}" for x, y in points) + " z", fill)

    def icon(self, name: str, x: float, y: float, size: float, fill: str) -> None:
        """Glyphe aus dem Designsystem (24×24) an (x, y) in der Größe ``size``."""
        if name not in self._icons:
            raise KeyError(f"Unbekannte Glyphe: {name}")
        self.path(self._icons[name], fill, transform=f"translate({num(x)} {num(y)}) scale({num(size / 24)})")

    # Zusammengesetzte Motive --------------------------------------------------------------------------------------------
    def backdrop(self) -> None:
        self.rect(0, 0, WIDTH, HEIGHT, 18, fill="SurfaceHigh")

    def window(self, x: float, y: float, w: float, h: float, bar: str = "Sidebar") -> None:
        """Fenster mit Titelleiste."""
        self.rect(x, y, w, h, 10, fill="Surface", stroke="Separator", width=1.5)
        self.path(f"M{num(x + 1)},{num(y + 22)} h{num(w - 2)}", stroke="Separator", width=1.5)
        self.rect(x + w - 34, y + 9, 6, 6, 3, fill=bar)
        self.rect(x + w - 22, y + 9, 6, 6, 3, fill=bar)

    def text_lines(self, x: float, y: float, widths: list[float], gap: float = 12, token: str = "Separator", height: float = 5) -> None:
        for i, w in enumerate(widths):
            self.rect(x, y + i * gap, w, height, height / 2, fill=token)

    def wave(self, x: float, cy: float, heights: list[float], token: str = "Accent", step: float = 7, width: float = 3.5) -> None:
        for i, h in enumerate(heights):
            self.rect(x + i * step, cy - h / 2, width, h, width / 2, fill=token)

    def badge(self, cx: float, cy: float, r: float, icon: str, fill: str = "Accent", glyph: str = "OnAccent") -> None:
        self.circle(cx, cy, r, fill=fill)
        size = r * 1.15
        self.icon(icon, cx - size / 2, cy - size / 2, size, glyph)

    def key(self, x: float, y: float, w: float, h: float = 30, pressed: bool = False) -> None:
        """Taste; gedrückt in Akzent-Fläche."""
        self.rect(x, y + 3, w, h, 7, fill="Separator")
        self.rect(x, y + (3 if pressed else 0), w, h, 7, fill="Selection" if pressed else "Surface", stroke="Separator", width=1.5)

    def arrow(self, x1: float, y1: float, x2: float, y2: float, token: str = "Accent", width: float = 3) -> None:
        self.line([(x1, y1), (x2, y2)], token, width)
        # Pfeilspitze in Richtung (x2, y2).
        dx, dy = x2 - x1, y2 - y1
        length = max((dx * dx + dy * dy) ** 0.5, 1e-6)
        ux, uy = dx / length, dy / length
        size = 7
        left = (x2 - ux * size - uy * size * 0.8, y2 - uy * size + ux * size * 0.8)
        right = (x2 - ux * size + uy * size * 0.8, y2 - uy * size - ux * size * 0.8)
        self.line([left, (x2, y2), right], token, width)

    def svg(self) -> str:
        body = "\n".join(self._parts)
        return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{WIDTH}" height="{HEIGHT}" viewBox="0 0 {WIDTH} {HEIGHT}">\n'
                f"{body}\n</svg>\n")


_HEX = re.compile(r"#[0-9A-F]{6}")


def colors_used(svg: str) -> list[tuple[str, str]]:
    """(Token, Farbe) je Füllung bzw. Strich einer erzeugten Datei – für die Prüfung „nur Loom-Tokens“."""
    pairs = re.findall(r'(fill|stroke)="(#[0-9A-Fa-f]{6})" data-\1="([A-Za-z.]+)"', svg)
    return [(token, color.upper()) for _, color, token in pairs]
