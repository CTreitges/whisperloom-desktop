"""ASGI-Middleware: Sicherheits-Header und Größengrenze für Anfragen (docs/server-api.md §1).

Basis: whisperloom-android feat/simple-portal server/simple-whisper/sicherheit.py
"""

import asyncio
import json

from . import vorab
from .fehler import FEHLER
from .log import ereignis

MAX_BODY = 64 * 1024
# Proxy (docs/server-api.md §3.7): Audio bis 25 MiB plus Multipart-Rahmen, Text bis 1 MiB.
GRENZEN = {
    "/api/v1/proxy/audio/transcriptions": 25 * 1024 * 1024 + 64 * 1024,
    "/api/v1/proxy/chat/completions": 1024 * 1024,
}


# Lese-Zeitlimits für jeden Body (Slowloris): höchstens so lange ohne neues Datenpaket bzw. insgesamt. Ein 25-MiB-Upload
# bei 1 Mbit/s braucht gut 200 s. Danach: 400 invalid_request. Große Proxy-Bodies liest der Server erst nach der
# Vorab-Prüfung des Geräte-Tokens und nur mit freiem Slot je Gerät/Firma/Instanz (vorab.py, Prüfrunde 1 Befund 1).
LESE_LEERLAUF_S = 15.0
LESE_GESAMT_S = 300.0
LESE_GESAMT_KLEIN_S = 30.0


def grenze(pfad: str) -> int:
    return GRENZEN.get(pfad, MAX_BODY)

CSP = (
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
    "connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'"
)

HEADER = [
    (b"content-security-policy", CSP.encode()),
    (b"strict-transport-security", b"max-age=31536000"),
    (b"x-content-type-options", b"nosniff"),
    (b"x-frame-options", b"DENY"),
    # same-origin statt no-referrer: sonst senden Browser bei Formular-POSTs „Origin: null“ (CSRF-Prüfung schlägt fehl).
    (b"referrer-policy", b"same-origin"),
    (b"permissions-policy", b"camera=(), microphone=(), geolocation=()"),
    (b"cross-origin-opener-policy", b"same-origin"),
]


class SicherheitsMiddleware:
    """Liest den Body vorab (höchstens [grenze] je Pfad, auch ohne Content-Length) und ergänzt die Header jeder Antwort."""

    def __init__(self, app):
        self.app = app
        self.slots = vorab.Slots()

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        laenge = dict(scope.get("headers", [])).get(b"content-length")
        hoechstens = grenze(scope["path"])
        if laenge is not None and (not laenge.isdigit() or int(laenge) > hoechstens):
            await self._zu_gross(scope, send)
            return
        gross = hoechstens > MAX_BODY and (laenge is None or int(laenge) > MAX_BODY)
        geraet = None
        if gross:
            try:
                geraet = vorab.geraet_pruefen(scope)
                self.slots.belegen(*geraet)
            except vorab.Abgelehnt as e:
                extra = [(b"retry-after", str(e.retry_after).encode())] if e.retry_after else None
                await self._antwort(scope, send, FEHLER[e.code][0], e.code, FEHLER[e.code][1], extra)
                return
        try:
            body = await self._lesen(scope, receive, send, hoechstens, LESE_GESAMT_S if gross else LESE_GESAMT_KLEIN_S)
        finally:
            if geraet is not None:
                self.slots.freigeben(*geraet)  # Slot nur für die Lesephase; danach begrenzt proxy.py je Firma
        if body is not None:
            await self._verarbeiten(scope, receive, send, body)

    async def _lesen(self, scope, receive, send, hoechstens: int, gesamt_s: float) -> bytes | None:
        """Body vollständig lesen; None, wenn schon geantwortet wurde (zu groß, Zeitlimit) oder der Client abbrach."""
        teile: list[bytes] = []
        gelesen = 0
        try:
            # Doku: https://docs.python.org/3/library/asyncio-task.html#asyncio.timeout (abgerufen 2026-11-01)
            async with asyncio.timeout(gesamt_s):
                while True:
                    nachricht = await asyncio.wait_for(receive(), LESE_LEERLAUF_S)
                    if nachricht["type"] != "http.request":
                        return None  # Client hat abgebrochen
                    teile.append(nachricht.get("body", b""))
                    gelesen += len(teile[-1])
                    if gelesen > hoechstens:
                        await self._zu_gross(scope, send)
                        return None
                    if not nachricht.get("more_body", False):
                        return b"".join(teile)
        except TimeoutError:
            ereignis("anfrage abgebrochen", fehler="lese_zeitlimit", route=scope["path"][:40])
            await self._antwort(scope, send, 400, "invalid_request", "Die Anfrage kam nicht rechtzeitig an.")
            return None

    async def _verarbeiten(self, scope, receive, send, body: bytes):
        geliefert = False

        async def wiedergabe():
            nonlocal geliefert
            if not geliefert:
                geliefert = True
                return {"type": "http.request", "body": body, "more_body": False}
            return await receive()

        statisch = scope["path"].startswith("/static/")
        gestartet = False

        async def mit_headern(nachricht):
            nonlocal gestartet
            if nachricht["type"] == "http.response.start":
                gestartet = True
                headers = list(nachricht.get("headers", []))
                vorhanden = {k.lower() for k, _ in headers}
                headers += [(k, v) for k, v in HEADER if k not in vorhanden]
                if not statisch and b"cache-control" not in vorhanden:
                    headers.append((b"cache-control", b"no-store"))
                nachricht = {**nachricht, "headers": headers}
            await send(nachricht)

        try:
            await self.app(scope, wiedergabe, mit_headern)
        except Exception as exc:
            # Nur der Typ ins Log (T9): Ausnahmetexte und Tracebacks können Eingaben oder Keys enthalten.
            # Nicht weiterwerfen, sonst schreibt uvicorn den Traceback samt Text ins Journal.
            ereignis("fehler intern", fehler=type(exc).__name__)
            if not gestartet:
                await self._antwort(scope, send, 500, "internal_error", "Da ist etwas schiefgegangen.")

    @classmethod
    async def _zu_gross(cls, scope, send):
        await cls._antwort(scope, send, 413, "payload_too_large", "Die Anfrage ist zu groß.")

    @staticmethod
    async def _antwort(scope, send, status: int, code: str, text: str, extra: list | None = None):
        if scope["path"].startswith("/api/"):
            body = json.dumps({"error": code, "message": FEHLER[code][1]}).encode()
            typ = b"application/json"
        else:
            body, typ = text.encode(), b"text/plain; charset=utf-8"
        headers = [(b"content-type", typ), (b"content-length", str(len(body)).encode()), *HEADER, *(extra or [])]
        await send({"type": "http.response.start", "status": status, "headers": headers})
        await send({"type": "http.response.body", "body": body})
