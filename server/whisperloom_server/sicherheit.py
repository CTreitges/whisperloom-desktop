"""ASGI-Middleware: Sicherheits-Header und Größengrenze für Anfragen (docs/server-api.md §1).

Basis: whisperloom-android feat/simple-portal server/simple-whisper/sicherheit.py
"""

import json

from .fehler import FEHLER
from .log import ereignis

MAX_BODY = 64 * 1024

CSP = (
    "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; "
    "connect-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'"
)

HEADER = [
    (b"content-security-policy", CSP.encode()),
    (b"strict-transport-security", b"max-age=31536000"),
    (b"x-content-type-options", b"nosniff"),
    (b"x-frame-options", b"DENY"),
    (b"referrer-policy", b"no-referrer"),
    (b"permissions-policy", b"camera=(), microphone=(), geolocation=()"),
    (b"cross-origin-opener-policy", b"same-origin"),
]


class SicherheitsMiddleware:
    """Liest den Body vorab (höchstens MAX_BODY, auch ohne Content-Length) und ergänzt die Header jeder Antwort."""

    def __init__(self, app):
        self.app = app

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            await self.app(scope, receive, send)
            return

        laenge = dict(scope.get("headers", [])).get(b"content-length")
        if laenge is not None and (not laenge.isdigit() or int(laenge) > MAX_BODY):
            await self._zu_gross(scope, send)
            return

        teile: list[bytes] = []
        gelesen = 0
        while True:
            nachricht = await receive()
            if nachricht["type"] != "http.request":
                return  # Client hat abgebrochen
            teile.append(nachricht.get("body", b""))
            gelesen += len(teile[-1])
            if gelesen > MAX_BODY:
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
    async def _antwort(scope, send, status: int, code: str, text: str):
        if scope["path"].startswith("/api/"):
            body = json.dumps({"error": code, "message": FEHLER[code][1]}).encode()
            typ = b"application/json"
        else:
            body, typ = text.encode(), b"text/plain; charset=utf-8"
        headers = [(b"content-type", typ), (b"content-length", str(len(body)).encode()), *HEADER]
        await send({"type": "http.response.start", "status": status, "headers": headers})
        await send({"type": "http.response.body", "body": body})
