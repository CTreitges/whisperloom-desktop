"""Ereignis-Log ohne Inhalte: nur feste Feldnamen, Werte als kurze, harmlose Zeichenketten.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/log.py
"""

import logging
import re

log = logging.getLogger("whisperloom_server")

FELDER = {"geraet", "firma", "admin", "anbieter", "ms", "fehler", "status", "art", "route", "http", "zugang"}
_SICHER = re.compile(r"[^A-Za-z0-9_.:-]")


def ereignis(name: str, **felder: object) -> None:
    teile = [name]
    for k, v in felder.items():
        if k not in FELDER:
            raise ValueError(f"Feld {k} ist im Log nicht erlaubt")
        teile.append(f"{k}={_SICHER.sub('_', str(v))[:40]}")
    log.info(" ".join(teile))


def einrichten() -> None:
    logging.getLogger("httpx").setLevel(logging.WARNING)
    logging.getLogger("httpcore").setLevel(logging.WARNING)
    if log.handlers:
        return
    h = logging.StreamHandler()
    h.setFormatter(logging.Formatter("%(levelname)s %(message)s"))
    log.addHandler(h)
    log.setLevel(logging.INFO)
    log.propagate = False
