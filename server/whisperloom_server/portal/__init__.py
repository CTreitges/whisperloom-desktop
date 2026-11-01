"""Admin-Portal (Jinja2 + CSS, ohne Build-Schritt). Im Datei-Modus nicht eingebunden."""

from fastapi import APIRouter

from . import anmeldung, einstellungen, geraete, instanz, nutzung, verwaltung

router = APIRouter()
for _teil in (anmeldung, geraete, einstellungen, nutzung, verwaltung, instanz):
    router.include_router(_teil.router)
