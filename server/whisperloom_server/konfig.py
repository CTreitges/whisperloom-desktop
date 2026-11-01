"""Konfiguration eines Geräts für GET /api/v1/config (docs/server-api.md §3.3) samt Version für ETag/304."""

import hashlib
import json
import sqlite3

from . import richtlinie, schluessel
from .krypto import Krypto

SYNC_INTERVALL_S = 900
ROTATION_MS = 90 * 24 * 3600 * 1000


def rotation_faellig(geraet: sqlite3.Row, jetzt: int) -> bool:
    return geraet["token_ms"] < jetzt - ROTATION_MS


def _fest(settings: dict, levels: dict, pfad: str, wert) -> None:
    abschnitt, feld = pfad.split(".")
    settings.setdefault(abschnitt, {})[feld] = wert
    levels[pfad] = "fixed"


def bauen(con: sqlite3.Connection, kry: Krypto, basis_url: str, firma: sqlite3.Row, geraet: sqlite3.Row, jetzt: int) -> dict:
    """Ohne configVersion; Keys nur im Verteil-Modus (Aufrufer prüft vorher die Freigabe)."""
    settings, policy = richtlinie.als_konfig(richtlinie.aufloesen(con, firma["id"], geraet["gruppe_id"]))
    levels = policy["levels"]
    daten: dict = {
        "tenantName": firma["name"],
        "keyMode": firma["key_modus"],
        "syncIntervalSec": SYNC_INTERVALL_S,
        "rotateToken": rotation_faellig(geraet, jetzt),
    }
    if firma["key_modus"] == "proxy":
        proxy = f"{basis_url}/api/v1/proxy"
        daten["proxy"] = {"baseUrl": proxy}
        # Erkennung und Text laufen über den Proxy; Ersatz-Anbieter wählt der Server selbst (W2-SRV2).
        _fest(settings, levels, "stt.providerId", "custom")
        _fest(settings, levels, "stt.baseUrl", proxy)
        _fest(settings, levels, "llm.providerId", "same")
        _fest(settings, levels, "sttFallback.enabled", False)
        _fest(settings, levels, "llmFallback.enabled", False)
    else:
        secrets, zugaenge = schluessel.fuer_geraet(con, kry, firma["id"])
        daten["secrets"] = secrets
        for zugang, (anbieter, url) in zugaenge.items():
            abschnitt = schluessel.ABSCHNITT[zugang]
            if f"{abschnitt}.providerId" not in levels or levels[f"{abschnitt}.providerId"] != "fixed":
                _fest(settings, levels, f"{abschnitt}.providerId", anbieter)
                if url:
                    _fest(settings, levels, f"{abschnitt}.baseUrl", url)
    daten["settings"] = settings
    daten["policy"] = policy
    return daten


def version(daten: dict) -> str:
    roh = json.dumps(daten, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    return "c_" + hashlib.sha256(roh).hexdigest()[:16]


def mit_version(daten: dict) -> dict:
    return {"configVersion": version(daten), **daten}
