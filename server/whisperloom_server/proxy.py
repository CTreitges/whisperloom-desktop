"""Proxy-Routen (docs/server-api.md §3.6/§3.7, W2-SRV2): nach außen OpenAI-kompatibel, Anmeldung mit dem Geräte-Token,
nach innen die Zugänge der eigenen Firma (stt/stt_fallback, llm/llm_fallback) über die Adapter in upstream.py.

Datenschutz: Audio und Text liegen nur im Arbeitsspeicher dieser Anfrage; gespeichert werden nur Zähler (nutzung.py).
Logs: nur Metadaten (Gerät, Firma, Zugang, Anbieter, HTTP-Status, Dauer, Fehlerklasse) – nie Inhalt, Header oder Keys.
Antworten von Anbietern gehen nie roh an das Gerät (sie könnten Teile des Keys enthalten), nur normalisiert.
Zeitlimits wie die App: Verbindung 15 s, mit Ersatz-Zugang 8 s; Antwort 90 s, eigene Server 600 s (upstream.lese_zeit_s).
"""

import asyncio
import json
import math
import sqlite3
import time

import httpx
from fastapi import APIRouter, Depends, Request

from . import budget, nutzung, schluessel, ssrf, upstream
from .abhaengig import jetzt, verbindung
from .api_v1 import geraet_aus_token
from .fehler import ApiFehler
from .log import ereignis
from .upload import audio_dauer_ms, multipart_im_thread, multipart_lesen  # noqa: F401 (Tests)

router = APIRouter(prefix="/api/v1/proxy")
MAX_AUDIO = 25 * 1024 * 1024  # §3.7; die Middleware begrenzt den ganzen Body (sicherheit.GRENZEN)
MAX_ANTWORT = 8 * 1024 * 1024
VERBINDEN_S = 15.0
VERBINDEN_MIT_ERSATZ_S = 8.0
MAX_GLEICHZEITIG_JE_FIRMA = 20


class _Ersatz(Exception):
    """Fehlschlag, bei dem der Ersatz-Zugang einspringt (Netz, Zeitlimit, 401/403/429, 5xx, unbrauchbare Antwort)."""

    def __init__(self, code: str, http: int = 0, grund: str = ""):
        super().__init__(code)
        self.code, self.http, self.grund = code, http, grund


class _Ende(_Ersatz):
    """Fehlschlag wegen der Anfrage selbst (400/404/413/422 des Anbieters): kein Ersatz."""


def _zulassen(g: sqlite3.Row) -> None:
    if g["firma_gesperrt"]:
        raise ApiFehler("tenant_blocked")
    if g["status"] == "pending":
        raise ApiFehler("device_pending")
    if g["status"] == "blocked":
        raise ApiFehler("device_blocked")
    if g["key_modus"] != "proxy":
        raise ApiFehler("not_found")  # Verteil-Modus: der Server bietet keinen Proxy an (Vertrag §1.2)


def _kette(request: Request, con: sqlite3.Connection, firma_id: str, art: str) -> list[dict]:
    """Zugänge in Reihenfolge, nur aus der Firma des Geräts. Text ohne eigenen llm-Zugang nutzt den der Erkennung."""
    kry = request.app.state.krypto
    z = {n: schluessel.zugang(con, kry, firma_id, n) for n in schluessel.ZUGAENGE}
    if art == "stt":
        kette, kann = [z["stt"], z["stt_fallback"]], upstream.kann_erkennung
    elif z["llm"] is not None:
        kette, kann = [z["llm"], z["llm_fallback"]], upstream.kann_text
    else:
        # Erkennungs-Zugang für Text: dessen festes Modell ist ein Erkennungsmodell → Modell der Anfrage nehmen.
        geliehen = [{**k, "modell": ""} if k is not None else None for k in (z["stt"], z["stt_fallback"])]
        kette, kann = [geliehen[0], z["llm_fallback"] or geliehen[1]], upstream.kann_text
    return [k for k in kette if k is not None and kann(k) and upstream.basis(k)]


async def _lesen(r: httpx.Response) -> bytes:
    teile, n = [], 0
    async for stueck in r.aiter_bytes():
        n += len(stueck)
        if n > MAX_ANTWORT:
            raise _Ersatz("upstream_error", r.status_code, "zu_gross")
        teile.append(stueck)
    return b"".join(teile)


async def _senden(request: Request, zugang: dict, a: upstream.Anfrage, verbinden_s: float) -> tuple[int, bytes]:
    einst = request.app.state.einst
    try:
        ziel = await asyncio.to_thread(ssrf.pruefen, a.url, einst.upstream_erlaubt, einst.aufloeser)
    except ssrf.ZielVerboten as e:
        raise _Ersatz("upstream_error", 0, "ssrf_" + str(e)) from e
    kopf = {**a.headers, "Host": ziel.host_header, "User-Agent": "WhisperLoom-Server"}
    erweiterung = {"sni_hostname": ziel.sni} if ziel.sni else None
    zeit = httpx.Timeout(upstream.lese_zeit_s(zugang), connect=verbinden_s)
    try:
        # httpx' Lese-Zeitlimit gilt je Block; die Gesamt-Deadline beendet auch eine tröpfelnde Antwort.
        # Doku: https://docs.python.org/3/library/asyncio-task.html#asyncio.timeout (abgerufen 2026-11-01)
        async with asyncio.timeout(upstream.lese_zeit_s(zugang) + verbinden_s):
            async with httpx.AsyncClient(follow_redirects=False, trust_env=False, timeout=zeit) as c:
                dateien = {"file": a.datei} if a.datei else None
                anfrage = c.build_request("POST", ziel.url, headers=kopf, json=a.json, data=a.daten, files=dateien,
                                          extensions=erweiterung)
                r = await c.send(anfrage, stream=True)
                try:
                    return r.status_code, await _lesen(r)
                finally:
                    await r.aclose()
    except (httpx.TimeoutException, TimeoutError) as e:
        raise _Ersatz("upstream_timeout", 0, "zeit") from e
    except httpx.HTTPError as e:
        raise _Ersatz("upstream_error", 0, "netz") from e


def _json(status: int, roh: bytes):
    try:
        return json.loads(roh)
    except ValueError as e:
        raise _Ersatz("upstream_error", status, "kein_json") from e


async def _versuch(request: Request, zugang: dict, a: upstream.Anfrage, verbinden_s: float):
    status, roh = await _senden(request, zugang, a, verbinden_s)
    if status == 400 and a.json is not None and upstream.protokoll(zugang) == "openai":
        weg = upstream.abgelehnte_felder(a.json, roh[:4000].decode("utf-8", "replace"))
        if weg:
            a.json = {k: v for k, v in a.json.items() if k not in weg}
            status, roh = await _senden(request, zugang, a, verbinden_s)
    if status in (401, 403, 429) or status >= 500:
        raise _Ersatz("upstream_error", status, "http")
    if status != 200:
        raise _Ende("upstream_error", status, "http")
    return _json(status, roh)


async def _weiterleiten(request: Request, con: sqlite3.Connection, g: sqlite3.Row, art: str, modell_wunsch: str,
                        bauen, auswerten, audio_ms: int = 0) -> dict:
    zeit = jetzt(request)
    ebene = budget.erschoepft(con, g, zeit)
    if ebene is not None:
        nutzung.fehler(con, g, zeit, "budget")
        ereignis("proxy abgelehnt", route=art, geraet=g["id"], firma=g["firma_id"], fehler="budget", art=ebene)
        raise ApiFehler("budget_exhausted")
    kette = _kette(request, con, g["firma_id"], art)
    if not kette:
        ereignis("proxy abgelehnt", route=art, geraet=g["id"], firma=g["firma_id"], fehler="kein_zugang")
        raise ApiFehler("not_found")
    # Gleichzeitige Anbieter-Anfragen je Firma begrenzen: ein langsamer Anbieter einer Firma bindet nicht den Server.
    laufend = request.app.state.__dict__.setdefault("proxy_laufend", {})
    if laufend.get(g["firma_id"], 0) >= MAX_GLEICHZEITIG_JE_FIRMA:
        ereignis("proxy abgelehnt", route=art, geraet=g["id"], firma=g["firma_id"], fehler="parallel")
        raise ApiFehler("rate_limited", retry_after=5)
    laufend[g["firma_id"]] = laufend.get(g["firma_id"], 0) + 1
    try:
        return await _kette_abarbeiten(request, con, g, art, kette, modell_wunsch, bauen, auswerten, audio_ms, zeit)
    finally:
        laufend[g["firma_id"]] -= 1


async def _kette_abarbeiten(request: Request, con: sqlite3.Connection, g: sqlite3.Row, art: str, kette: list[dict],
                            modell_wunsch: str, bauen, auswerten, audio_ms: int, zeit: int) -> dict:
    verbinden = VERBINDEN_MIT_ERSATZ_S if len(kette) > 1 else VERBINDEN_S
    code = "upstream_error"
    for zugang in kette:
        modell = zugang["modell"] or modell_wunsch
        start = time.monotonic()
        try:
            daten = await _versuch(request, zugang, bauen(zugang, modell), verbinden)
            ergebnis = auswerten(zugang, modell, daten)
        except upstream.AdapterFehler:
            code, ende, http, grund = "upstream_error", False, 200, "antwort"
        except _Ersatz as e:
            code, ende, http, grund = e.code, isinstance(e, _Ende), e.http, e.grund
        else:
            ms = int((time.monotonic() - start) * 1000)
            nutzung.erfassen(con, g, zeit, modell, audio_ms, ergebnis.tokens_ein, ergebnis.tokens_aus)
            ereignis("proxy ok", route=art, geraet=g["id"], firma=g["firma_id"], zugang=zugang["name"],
                     anbieter=zugang["anbieter"], http=200, ms=ms)
            return ergebnis.antwort
        ereignis("proxy fehler", route=art, geraet=g["id"], firma=g["firma_id"], zugang=zugang["name"],
                 anbieter=zugang["anbieter"], http=http, ms=int((time.monotonic() - start) * 1000), fehler=grund)
        if ende:
            break
    nutzung.fehler(con, g, zeit, code)
    raise ApiFehler(code)


def _ungueltig(con: sqlite3.Connection, g: sqlite3.Row, request: Request) -> ApiFehler:
    nutzung.fehler(con, g, jetzt(request), "invalid_request")
    return ApiFehler("invalid_request")


@router.get("/models")
def models(g=Depends(geraet_aus_token), con: sqlite3.Connection = Depends(verbindung)):
    _zulassen(g)
    daten, gesehen = [], set()
    for r in schluessel.liste(con, g["firma_id"]):
        if r["zugang"] in schluessel.ZUGAENGE and r["modell"] and r["modell"] not in gesehen:
            gesehen.add(r["modell"])
            daten.append({"id": r["modell"], "object": "model", "owned_by": r["anbieter"]})
    return {"object": "list", "data": daten}


@router.post("/audio/transcriptions")
async def transkription(request: Request, g=Depends(geraet_aus_token), con: sqlite3.Connection = Depends(verbindung)):
    _zulassen(g)
    try:
        felder, wav = await multipart_im_thread(request.headers.get("content-type", ""), await request.body())
    except (ValueError, UnicodeError):
        raise _ungueltig(con, g, request) from None
    if len(wav) > MAX_AUDIO:
        raise ApiFehler("payload_too_large")
    dauer = audio_dauer_ms(wav)
    if dauer is None:  # Vertrag §3.7: WAV; nur so lässt sich die Dauer (Budget, Statistik) bestimmen
        raise _ungueltig(con, g, request)
    felder["response_format"] = "json"  # Der Proxy antwortet immer als JSON {"text": …}
    wunsch = felder.pop("model", "")

    def bauen(zugang: dict, modell: str) -> upstream.Anfrage:
        return upstream.erkennung_anfrage(zugang, {**felder, "model": modell}, wav)

    def auswerten(zugang: dict, _modell: str, daten):
        return upstream.erkennung_antwort(zugang, daten)

    return await _weiterleiten(request, con, g, "stt", wunsch, bauen, auswerten, dauer)


def _zahl(wert, unten: float, oben: float) -> bool:
    return isinstance(wert, (int, float)) and not isinstance(wert, bool) and math.isfinite(wert) and unten <= wert <= oben


def chat_pruefen(body) -> bool:
    if not isinstance(body, dict) or set(body) - upstream.CHAT_FELDER or body.get("stream") not in (None, False):
        return False
    nachrichten = body.get("messages")
    if not isinstance(nachrichten, list) or not 1 <= len(nachrichten) <= 100:
        return False
    for n in nachrichten:
        if not isinstance(n, dict) or set(n) != {"role", "content"} or n["role"] not in upstream.ROLLEN \
                or not isinstance(n["content"], str):
            return False
    if nachrichten[0]["role"] == "assistant":
        return False
    pruefungen = {
        "model": lambda w: isinstance(w, str) and len(w) <= 100,
        "temperature": lambda w: _zahl(w, 0, 2),
        "top_p": lambda w: _zahl(w, 0, 1),
        "max_tokens": lambda w: isinstance(w, int) and 1 <= w <= 200_000,
        "max_completion_tokens": lambda w: isinstance(w, int) and 1 <= w <= 200_000,
        "reasoning_effort": lambda w: isinstance(w, str) and len(w) <= 20,
        "thinking": lambda w: isinstance(w, dict) and len(w) <= 4 and all(isinstance(k, str) for k in w),
        "stream": lambda w: w is False,
    }
    return all(pruefungen[k](v) for k, v in body.items() if k != "messages")


@router.post("/chat/completions")
async def chat(request: Request, g=Depends(geraet_aus_token), con: sqlite3.Connection = Depends(verbindung)):
    _zulassen(g)
    try:
        body = await request.json()
    except ValueError:
        body = None
    if not chat_pruefen(body):
        raise _ungueltig(con, g, request)
    wunsch = body.get("model", "")

    def bauen(zugang: dict, modell: str) -> upstream.Anfrage:
        return upstream.chat_anfrage(zugang, {**body, "model": modell})

    return await _weiterleiten(request, con, g, "chat", wunsch, bauen, upstream.chat_antwort)
