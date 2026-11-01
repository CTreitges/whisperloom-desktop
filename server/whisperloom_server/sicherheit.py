"""ASGI-Middleware: Sicherheits-Header und Größengrenze für Anfragen (docs/server-api.md §1).

Basis: whisperloom-android feat/simple-portal server/simple-whisper/sicherheit.py
"""

import json

from .fehler import FEHLER
from .log import ereignis

MAX_BODY = 64 * 1024
# Proxy (docs/server-api.md §3.7): Audio bis 25 MiB plus Multipart-Rahmen, Text bis 1 MiB.
GRENZEN = {
    "/api/v1/proxy/audio/transcriptions": 25 * 1024 * 1024 + 64 * 1024,
    "/api/v1/proxy/chat/completions": 1024 * 1024,
}


# Bodies über 64 KiB (nur Proxy) puffert der Server vor der Anmeldung – höchstens so viele gleichzeitig (Prüfer-Befund
# W2-SRV2 Nr. 3: anonyme parallele 25-MiB-Uploads binden sonst beliebig Arbeitsspeicher). Darüber: 503 mit Retry-After.
MAX_GROSSE_GLEICHZEITIG = 16


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
        self.grosse_laufend = 0

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
        if gross:
            if self.grosse_laufend >= MAX_GROSSE_GLEICHZEITIG:
                await self._antwort(scope, send, 503, "service_unavailable", "Bitte gleich erneut.", [(b"retry-after", b"5")])
                return
            self.grosse_laufend += 1
        try:
            await self._verarbeiten(scope, receive, send, hoechstens)
        finally:
            if gross:
                self.grosse_laufend -= 1

    async def _verarbeiten(self, scope, receive, send, hoechstens: int):
        teile: list[bytes] = []
        gelesen = 0
        while True:
            nachricht = await receive()
            if nachricht["type"] != "http.request":
                return  # Client hat abgebrochen
            teile.append(nachricht.get("body", b""))
            gelesen += len(teile[-1])
            if gelesen > hoechstens:
                await self._zu_gross(scope, send)
                return
            if not nachricht.get("more_body", False):
                break
        body = b"".join(teile)
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
