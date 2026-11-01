"""Fehlerklassen der Client-API (docs/server-api.md §1.2): {"error": code, "message": Text}.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/fehler.py
"""

from fastapi import Request
from fastapi.responses import JSONResponse

# code -> (HTTP-Status, Meldung als Rückfallebene)
FEHLER: dict[str, tuple[int, str]] = {
    "invalid_request": (400, "Die Anfrage ist ungültig."),
    "device_unknown": (401, "Dieses Gerät ist nicht (mehr) verbunden."),
    "code_invalid": (404, "Der Firmencode stimmt nicht."),
    "device_pending": (403, "Das Gerät wartet noch auf die Freigabe durch die IT."),
    "device_blocked": (403, "Das Gerät ist gesperrt."),
    "tenant_blocked": (403, "Die Firma ist gesperrt."),
    "leave_not_allowed": (403, "Abmelden ist von der Firma nicht erlaubt."),
    "budget_exhausted": (403, "Das Budget ist aufgebraucht."),
    "too_many_pending": (409, "Es warten zu viele Geräte auf Freigabe."),
    "payload_too_large": (413, "Die Anfrage ist zu groß."),
    "client_outdated": (426, "Die App ist zu alt."),
    "rate_limited": (429, "Zu viele Anfragen. Bitte später erneut."),
    "internal_error": (500, "Interner Fehler."),
    "upstream_error": (502, "Der Anbieter hat mit einem Fehler geantwortet."),
    "service_unavailable": (503, "Der Dienst ist gerade nicht verfügbar."),
    "upstream_timeout": (504, "Der Anbieter hat nicht rechtzeitig geantwortet."),
    "not_found": (404, "Nicht gefunden."),
}


class ApiFehler(Exception):
    """Wird in /api/v1 geworfen und von [fehler_antwort] in JSON übersetzt."""

    def __init__(self, code: str, retry_after: int | None = None):
        if code not in FEHLER:
            raise ValueError(f"unbekannter Fehlercode {code}")
        super().__init__(code)
        self.code = code
        self.retry_after = retry_after


def antwort(code: str, retry_after: int | None = None) -> JSONResponse:
    status, meldung = FEHLER[code]
    headers = {"Retry-After": str(retry_after)} if retry_after is not None else None
    return JSONResponse({"error": code, "message": meldung}, status_code=status, headers=headers)


async def fehler_antwort(_request: Request, exc: ApiFehler) -> JSONResponse:
    return antwort(exc.code, exc.retry_after)
