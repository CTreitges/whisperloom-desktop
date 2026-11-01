"""Gemeinsame FastAPI-Abhängigkeiten: DB-Verbindung je Anfrage, Uhr, Begrenzer.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/abhaengig.py
"""

import sqlite3
from collections.abc import Iterator

from fastapi import Request

from . import db
from .fehler import ApiFehler


def verbindung(request: Request) -> Iterator[sqlite3.Connection]:
    con = db.verbinden(request.app.state.einst.db_pfad)
    try:
        yield con
    finally:
        con.close()


def jetzt(request: Request) -> int:
    return request.app.state.einst.uhr()


def begrenzen(request: Request, schluessel: str, grenze: int, fenster_ms: int) -> None:
    """Für /api/v1: wirft 429 rate_limited mit Retry-After."""
    warten = request.app.state.begrenzer.erlaubt(schluessel, grenze, fenster_ms)
    if warten is not None:
        raise ApiFehler("rate_limited", retry_after=warten)
