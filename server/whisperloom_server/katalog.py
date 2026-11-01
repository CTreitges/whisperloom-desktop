"""Einstellungen, die eine Firma zentral setzen kann, gruppiert wie in der App (docs/settings-format.md).

Anzeigenamen stehen in texte/<sprache>.json unter `s.<pfad>` (Einstellung) und `g.<gruppe>` (Gruppe).
Pfade mit `kuerzel=True` (Tastenkürzel) sind nie Fest. Pfade unter `policy.` sind keine Einstellungswerte, sondern
Felder des Abschnitts `policy` (historyEnabled, updatesEnabled, allowedProviders).
"""

import re
from dataclasses import dataclass

ANBIETER = ("openai", "groq", "mistral", "elevenlabs", "together", "deepinfra", "openrouter", "anthropic", "gemini",
            "deepseek", "ollama", "custom")
STUFEN = ("off", "polish", "beautify", "summarize", "readable", "prompt")


@dataclass(frozen=True)
class Eintrag:
    pfad: str
    gruppe: str
    typ: str  # bool | wahl | text | zahl | liste
    optionen: tuple = ()
    kuerzel: bool = False
    max_laenge: int = 200


_KOMBI = re.compile(r"^((Ctrl|Alt|Shift|Win|Meta)\+){0,4}[A-Za-z0-9]{1,6}$")

EINTRAEGE: tuple[Eintrag, ...] = (
    # Allgemein
    Eintrag("desktop.uiLanguage", "allgemein", "wahl", ("", "de", "en")),
    # Freier ISO-Code; optionen = Vorschläge im Portal. "de+en" = Deutsch + Englisch (W2-PFIX, settings-format.md).
    Eintrag("language", "allgemein", "text", ("auto", "de", "en", "de+en"), max_laenge=5),
    Eintrag("engine", "allgemein", "wahl", ("online", "offline")),
    Eintrag("management.allowLeave", "allgemein", "bool"),
    Eintrag("policy.updatesEnabled", "allgemein", "bool"),
    # Erkennung
    Eintrag("stt.providerId", "erkennung", "wahl", ANBIETER),
    Eintrag("stt.baseUrl", "erkennung", "text"),
    Eintrag("stt.model", "erkennung", "text", max_laenge=100),
    Eintrag("stt.vocabulary", "erkennung", "text", max_laenge=4000),
    Eintrag("sttFallback.enabled", "erkennung", "bool"),
    Eintrag("sttFallback.providerId", "erkennung", "wahl", ANBIETER),
    Eintrag("sttFallback.baseUrl", "erkennung", "text"),
    Eintrag("sttFallback.model", "erkennung", "text", max_laenge=100),
    Eintrag("policy.allowedProviders", "erkennung", "liste", ANBIETER),
    # Textverbesserung
    Eintrag("refine.stage", "text", "wahl", STUFEN),
    Eintrag("refine.promptEnabled", "text", "bool"),
    Eintrag("refine.polishCleanup", "text", "wahl", ("plain", "clean", "readable")),
    Eintrag("refine.summarizeForm", "text", "wahl", ("auto", "prose")),
    Eintrag("refine.paragraphsPolish", "text", "bool"),
    Eintrag("refine.paragraphsBeautify", "text", "bool"),
    Eintrag("llm.providerId", "text", "wahl", ("same", *ANBIETER)),
    Eintrag("llm.baseUrl", "text", "text"),
    Eintrag("llm.model", "text", "text", max_laenge=100),
    Eintrag("llmFallback.enabled", "text", "bool"),
    Eintrag("llmFallback.providerId", "text", "wahl", ANBIETER),
    Eintrag("llmFallback.baseUrl", "text", "text"),
    Eintrag("llmFallback.model", "text", "text", max_laenge=100),
    # Text-Regeln
    Eintrag("polish.removeFillers", "regeln", "bool"),
    Eintrag("polish.autoCapitalize", "regeln", "bool"),
    Eintrag("polish.trailingSpace", "regeln", "bool"),
    Eintrag("polish.customFillers", "regeln", "liste"),
    # Offline
    Eintrag("offline.model", "offline", "wahl", ("tiny", "base", "small", "large-v3-turbo")),
    Eintrag("offline.accurate", "offline", "bool"),
    Eintrag("offline.refine", "offline", "wahl", ("local", "online_local", "skip")),
    # Ausgabe
    Eintrag("desktop.insertMode", "ausgabe", "wahl", ("paste", "type", "clipboard")),
    Eintrag("output.alwaysCopy", "ausgabe", "bool"),
    Eintrag("output.pressEnter", "ausgabe", "bool"),
    Eintrag("selection.floatingButton", "ausgabe", "bool"),
    Eintrag("selection.ctrlRightClick", "ausgabe", "bool"),
    # Verlauf
    Eintrag("history.enabled", "verlauf", "bool"),
    Eintrag("history.size", "verlauf", "zahl", (10, 25, 50, 100, 250, 500)),
    # Tastenkürzel: nie Fest
    Eintrag("desktop.hotkeysEnabled", "kuerzel", "bool", kuerzel=True),
    Eintrag("desktop.hotkeyHold", "kuerzel", "text", kuerzel=True, max_laenge=40),
    Eintrag("desktop.hotkeyToggle", "kuerzel", "text", kuerzel=True, max_laenge=40),
    Eintrag("desktop.hotkeyStage", "kuerzel", "text", kuerzel=True, max_laenge=40),
    Eintrag("desktop.hotkeyReinsert", "kuerzel", "text", kuerzel=True, max_laenge=40),
    Eintrag("desktop.hotkeyRewrite", "kuerzel", "text", kuerzel=True, max_laenge=40),
    Eintrag("selection.hotkeyCombo", "kuerzel", "text", kuerzel=True, max_laenge=40),
    # Netzwerk
    Eintrag("desktop.proxyMode", "netz", "wahl", ("system", "off", "manual")),
    Eintrag("desktop.proxyUrl", "netz", "text"),
)

GRUPPEN = ("allgemein", "erkennung", "text", "regeln", "offline", "ausgabe", "verlauf", "kuerzel", "netz")
NACH_PFAD = {e.pfad: e for e in EINTRAEGE}
# Eigene Anbieter (providers.custom) heißen in Zugängen "custom:<id>" (settings-format.md, W2-C0).
_ANBIETER_PFADE = {"stt.providerId", "sttFallback.providerId", "llm.providerId", "llmFallback.providerId"}
_EIGENER_ANBIETER = re.compile(r"custom:[a-z0-9][a-z0-9_-]{0,31}")
_URL_PFADE = {"stt.baseUrl", "sttFallback.baseUrl", "llm.baseUrl", "llmFallback.baseUrl", "desktop.proxyUrl"}


class Ungueltig(ValueError):
    """Text = Schlüssel in texte/*.json (Hinweis für das Formular)."""


def eintrag(pfad: str) -> Eintrag:
    e = NACH_PFAD.get(pfad)
    if e is None:
        raise Ungueltig("h.pfad_unbekannt")
    return e


def stufen_fuer(e: Eintrag) -> tuple[str, ...]:
    """Wählbare Stufen. policy.*-Felder und management.allowLeave kennen nur Fest/Frei (settings-format.md:
    management.* nie Vorgabe), Tastenkürzel nur Vorgabe/Frei."""
    if e.pfad.startswith(("policy.", "management.")):
        return ("fixed", "free")
    if e.kuerzel:
        return ("default", "free")
    return ("fixed", "default", "free")


def wert_pruefen(e: Eintrag, roh):
    """Formular- (str) oder Datei-Wert (JSON-Typ) → geprüfter JSON-Wert."""
    if e.typ == "bool":
        if isinstance(roh, bool):
            return roh
        if roh in ("true", "on", "1"):
            return True
        if roh in ("false", "off", "0", ""):
            return False
        raise Ungueltig("h.wert")
    if e.typ == "zahl":
        try:
            zahl = int(roh)
        except (TypeError, ValueError) as ex:
            raise Ungueltig("h.wert") from ex
        if e.optionen and zahl not in e.optionen:
            raise Ungueltig("h.wert")
        return zahl
    if e.typ == "liste":
        teile = roh if isinstance(roh, list) else str(roh).replace(",", "\n").splitlines()
        werte = list(dict.fromkeys(str(t).strip() for t in teile if str(t).strip()))
        if len(werte) > 200 or any(len(w) > 60 for w in werte):
            raise Ungueltig("h.wert")
        if e.optionen and any(w not in e.optionen for w in werte):
            raise Ungueltig("h.wert")
        return werte
    if not isinstance(roh, str):
        raise Ungueltig("h.wert")
    text = "".join(c for c in roh if c.isprintable() or c == "\n").strip()
    if len(text) > e.max_laenge:
        raise Ungueltig("h.wert")
    if e.typ == "wahl":
        if text not in e.optionen and not (e.pfad in _ANBIETER_PFADE and _EIGENER_ANBIETER.fullmatch(text)):
            raise Ungueltig("h.wert")
        return text
    if e.pfad == "language" and not re.fullmatch(r"auto|de\+en|[a-z]{2,3}", text):
        raise Ungueltig("h.wert")
    if e.pfad in _URL_PFADE and text and not re.fullmatch(r"https?://[^\s]+", text):
        raise Ungueltig("h.wert")
    if e.kuerzel and e.typ == "text" and text and not _KOMBI.match(text):
        raise Ungueltig("h.wert")
    return text
