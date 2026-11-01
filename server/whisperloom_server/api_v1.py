"""Client-API v1 (docs/server-api.md §3.1–3.5): Enrollment, Gerätezustand, Konfiguration, Token-Rotation, Abmelden.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/api_v1.py
Geräte-Token und Firmencodes nur als Hash in der DB. [geraet_aus_token] nutzt auch der Proxy (W2-SRV2).
"""

import sqlite3

from fastapi import APIRouter, Depends, Request, Response
from fastapi.responses import JSONResponse
from pydantic import BaseModel, ConfigDict, Field

from . import firmen, konfig, krypto, richtlinie
from .abhaengig import begrenzen, jetzt, verbindung
from .fehler import ApiFehler
from .log import ereignis
from .netz import client_ip
from .ratelimit import MINUTE, VIERTELSTUNDE

router = APIRouter(prefix="/api/v1")
POLL_S = 10
ALT_TOKEN_MS = 24 * 3600 * 1000


class Enrollment(BaseModel):
    model_config = ConfigDict(extra="forbid")
    code: str = Field(min_length=1, max_length=64)
    deviceName: str | None = Field(default=None, max_length=64)  # noqa: N815 (Vertragsfeld)
    platform: str = Field(min_length=1, max_length=20)
    appVersion: str | None = Field(default=None, max_length=20)  # noqa: N815


class Leer(BaseModel):
    model_config = ConfigDict(extra="forbid")


def _sauber(text: str) -> str:
    return "".join(c for c in text if c.isprintable()).strip()


@router.post("/enroll", status_code=201)
def enroll(body: Enrollment, request: Request, con: sqlite3.Connection = Depends(verbindung)):
    begrenzen(request, "enroll-ip:" + client_ip(request), 20, VIERTELSTUNDE)
    kry = request.app.state.krypto
    treffer = firmen.per_code(con, kry, body.code)
    if treffer is None:
        ereignis("enroll fehler", fehler="code_invalid")
        raise ApiFehler("code_invalid")
    if treffer["gesperrt"]:
        raise ApiFehler("tenant_blocked")
    zeit = jetzt(request)
    status = "approved" if treffer["auto_freigabe"] else "pending"
    if status == "pending":
        wartend = con.execute(
            "SELECT COUNT(*) FROM geraet WHERE firma_id = ? AND status = 'pending'", (treffer["id"],)
        ).fetchone()[0]
        if wartend >= treffer["max_wartend"]:
            raise ApiFehler("too_many_pending")
    plattform = _sauber(body.platform)
    name = _sauber(body.deviceName or "") or plattform
    gid = krypto.kurz_id("d_")
    tok = krypto.geraete_token()
    con.execute(
        "INSERT INTO geraet (id, firma_id, gruppe_id, token_hash, token_ms, name, plattform, app_version, status, "
        "angelegt_ms, zuletzt_ms) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
        (gid, treffer["id"], treffer["gruppe_id"], krypto.sha256(tok), zeit, name, plattform,
         _sauber(body.appVersion or ""), status, zeit, zeit),
    )
    ereignis("enroll ok", geraet=gid, firma=treffer["id"], status=status)
    return {"deviceId": gid, "deviceToken": tok, "state": status, "tenantName": treffer["name"], "pollAfterSec": POLL_S}


def geraet_aus_token(request: Request, con: sqlite3.Connection = Depends(verbindung)) -> sqlite3.Row:
    """Bearer → Geräte-Zeile (mit Firma). Ein neues Token löst beim ersten Gebrauch das alte ab (§3.4)."""
    kopf = request.headers.get("authorization", "")
    if not kopf.startswith("Bearer ") or len(kopf) > 200:
        raise ApiFehler("device_unknown")
    h = krypto.sha256(kopf.removeprefix("Bearer ").strip())
    zeit = jetzt(request)
    sql = (
        "SELECT g.*, f.name AS firma_name, f.gesperrt AS firma_gesperrt, f.key_modus, f.auto_freigabe, f.anmeldearten "
        "FROM geraet g JOIN firma f ON f.id = g.firma_id WHERE "
    )
    row = con.execute(sql + "g.token_neu_hash = ?", (h,)).fetchone()
    if row is not None:
        con.execute(
            "UPDATE geraet SET token_hash = token_neu_hash, token_ms = token_neu_ms, token_neu_hash = NULL, "
            "token_neu_ms = 0 WHERE id = ?",
            (row["id"],),
        )
    else:
        row = con.execute(sql + "g.token_hash = ?", (h,)).fetchone()
        if row is None or (row["token_neu_hash"] and row["token_neu_ms"] < zeit - ALT_TOKEN_MS):
            raise ApiFehler("device_unknown")
    begrenzen(request, "geraet:" + row["id"], 120, MINUTE)
    con.execute("UPDATE geraet SET zuletzt_ms = ? WHERE id = ?", (zeit, row["id"]))
    return con.execute(sql + "g.id = ?", (row["id"],)).fetchone()


def _firma(con: sqlite3.Connection, firma_id: str) -> sqlite3.Row:
    return con.execute("SELECT * FROM firma WHERE id = ?", (firma_id,)).fetchone()


def _konfig(request: Request, con: sqlite3.Connection, g: sqlite3.Row) -> dict:
    return konfig.bauen(con, request.app.state.krypto, request.app.state.einst.basis_url, _firma(con, g["firma_id"]), g,
                        jetzt(request))


@router.get("/device")
def device(request: Request, g=Depends(geraet_aus_token), con: sqlite3.Connection = Depends(verbindung)):
    return {
        "deviceId": g["id"],
        "deviceName": g["name"],
        "state": g["status"],
        "tenantName": g["firma_name"],
        "configVersion": konfig.version(_konfig(request, con, g)),
        "rotateToken": konfig.rotation_faellig(g, jetzt(request)),
        "pollAfterSec": POLL_S,
    }


@router.get("/config")
def config(request: Request, g=Depends(geraet_aus_token), con: sqlite3.Connection = Depends(verbindung)):
    if g["firma_gesperrt"]:
        raise ApiFehler("tenant_blocked")
    if g["status"] == "pending":
        raise ApiFehler("device_pending")
    if g["status"] == "blocked":
        raise ApiFehler("device_blocked")
    daten = konfig.mit_version(_konfig(request, con, g))
    etag = f'"{daten["configVersion"]}"'
    angefragt = [t.strip().removeprefix("W/") for t in request.headers.get("if-none-match", "").split(",")]
    if etag in angefragt:
        return Response(status_code=304, headers={"ETag": etag})
    ereignis("config", geraet=g["id"], firma=g["firma_id"], art=g["key_modus"])
    return JSONResponse(daten, headers={"ETag": etag})


@router.post("/device/token")
def token_rotieren(request: Request, body: Leer | None = None, g=Depends(geraet_aus_token),
                   con: sqlite3.Connection = Depends(verbindung)):
    if g["status"] == "blocked" or g["firma_gesperrt"]:
        raise ApiFehler("device_blocked")
    tok = krypto.geraete_token()
    con.execute(
        "UPDATE geraet SET token_neu_hash = ?, token_neu_ms = ? WHERE id = ?", (krypto.sha256(tok), jetzt(request), g["id"])
    )
    ereignis("token rotiert", geraet=g["id"])
    return {"deviceToken": tok}


@router.post("/device/leave", status_code=204)
def leave(request: Request, body: Leer | None = None, g=Depends(geraet_aus_token),
          con: sqlite3.Connection = Depends(verbindung)):
    wirksam = richtlinie.aufloesen(con, g["firma_id"], g["gruppe_id"])
    if wirksam.get("management.allowLeave") == ("fixed", False):
        raise ApiFehler("leave_not_allowed")
    con.execute("DELETE FROM geraet WHERE id = ?", (g["id"],))
    ereignis("geraet abgemeldet", geraet=g["id"], firma=g["firma_id"])
    return Response(status_code=204)
