"""App-Fabrik. Start: `python -m whisperloom_server serve` bzw. `uvicorn --factory whisperloom_server.app:create_app`.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/app.py
Erweiterung (W2-SRV2): weitere Router in ROUTER bzw. nach create_app per app.include_router einhängen.
"""

from contextlib import asynccontextmanager
from pathlib import Path

from fastapi import FastAPI, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import HTMLResponse, JSONResponse, PlainTextResponse
from fastapi.staticfiles import StaticFiles
from starlette.exceptions import HTTPException as StarletteHTTPException

from . import api_v1, dateimodus, db, fehler, log
from .config import Einstellungen, lade_master
from .krypto import Krypto
from .ratelimit import Begrenzer
from .sicherheit import SicherheitsMiddleware


def create_app(einst: Einstellungen | None = None) -> FastAPI:
    einst = einst or Einstellungen.aus_umgebung()
    einst.pruefen()
    log.einrichten()
    db.einrichten(einst.db_pfad)

    @asynccontextmanager
    async def lebensdauer(app: FastAPI):
        # Doku: https://fastapi.tiangolo.com/advanced/events/ (lifespan, abgerufen 2026-11-01)
        beobachter = getattr(app.state, "datei", None)
        if beobachter:
            beobachter.starten()
        yield
        if beobachter:
            beobachter.stoppen()

    app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None, lifespan=lebensdauer)
    app.state.einst = einst
    app.state.krypto = Krypto(lade_master(einst))
    app.state.begrenzer = Begrenzer(einst.uhr)

    app.add_exception_handler(fehler.ApiFehler, fehler.fehler_antwort)

    @app.exception_handler(RequestValidationError)
    async def ungueltig(request: Request, _exc: RequestValidationError):
        if request.url.path.startswith("/api/"):
            return fehler.antwort("invalid_request")
        return PlainTextResponse("Die Anfrage ist ungültig. / Invalid request.", status_code=400)

    @app.exception_handler(StarletteHTTPException)
    async def http_fehler(request: Request, exc: StarletteHTTPException):
        if request.url.path.startswith("/api/"):
            return fehler.antwort("not_found" if exc.status_code in (404, 405) else "invalid_request")
        if exc.status_code == 404:
            return HTMLResponse("<!doctype html><title>404</title><p>Diese Seite gibt es nicht. / Page not found.</p>", 404)
        return PlainTextResponse("Die Anfrage ist ungültig. / Invalid request.", status_code=exc.status_code)

    @app.get("/healthz")
    def healthz():
        return JSONResponse({"status": "ok"})

    app.include_router(api_v1.router)
    if einst.datei_modus:
        app.state.datei = dateimodus.Beobachter(einst, app.state.krypto)
        app.state.datei.laden(streng=True)
    else:
        from . import portal
        from .portal.basis import Weiter, weiter_antwort

        app.add_exception_handler(Weiter, weiter_antwort)
        app.include_router(portal.router)
        app.mount("/static", StaticFiles(directory=str(Path(__file__).parent / "static")), name="static")
    app.add_middleware(SicherheitsMiddleware)
    return app
