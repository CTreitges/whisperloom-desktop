"""Vorab-Prüfung großer Proxy-Uploads, bevor der Server den Body liest (W2-SRV2, Prüfrunde 1 Befund 1: Slowloris).

Ohne sie belegten anonyme, absichtlich langsame Uploads die Upload-Slots der ganzen Instanz. Jetzt gilt:
- Das Geräte-Token wird vor dem ersten Byte des Bodies geprüft (Hash-Lookup wie api_v1.geraet_aus_token; Fehlversuche
  je IP begrenzt). Unbekannt → 401, wartend/gesperrt/Firma gesperrt → 403, ohne Slot und ohne Lesen.
- Slots gelten nur für die Lesephase und sind je Gerät, je Firma und je Instanz begrenzt; ein Gerät bzw. eine Firma
  kann die anderen nicht aussperren. Das Lese-Zeitlimit setzt sicherheit.py (Leerlauf je Block, Gesamtzeit).
Die vollständige Prüfung (Token-Rotation, Key-Modus) macht danach wie bei jeder Route geraet_aus_token.
"""

from dataclasses import dataclass, field

from starlette.requests import Request

from . import db, krypto, netz
from .ratelimit import MINUTE

MAX_INSTANZ = 32
MAX_JE_FIRMA = 8
MAX_JE_GERAET = 2


class Abgelehnt(Exception):
    def __init__(self, code: str, retry_after: int | None = None):
        super().__init__(code)
        self.code, self.retry_after = code, retry_after


def geraet_pruefen(scope) -> tuple[str, str]:
    """→ (Geräte-ID, Firmen-ID) eines freigegebenen Geräts einer nicht gesperrten Firma, sonst Abgelehnt."""
    app = scope["app"]
    request = Request(scope)
    kopf = request.headers.get("authorization", "")
    row = None
    if kopf.startswith("Bearer ") and len(kopf) <= 200:
        h = krypto.sha256(kopf.removeprefix("Bearer ").strip())
        con = db.verbinden(app.state.einst.db_pfad)
        try:
            row = con.execute(
                "SELECT g.id, g.firma_id, g.status, f.gesperrt FROM geraet g JOIN firma f ON f.id = g.firma_id "
                "WHERE g.token_hash = ? OR g.token_neu_hash = ?", (h, h),
            ).fetchone()
        finally:
            con.close()
    if row is None:
        warten = app.state.begrenzer.erlaubt("token-ip:" + netz.limit_ip(request), 60, MINUTE)
        raise Abgelehnt("rate_limited", warten) if warten is not None else Abgelehnt("device_unknown")
    if row["gesperrt"]:
        raise Abgelehnt("tenant_blocked")
    if row["status"] != "approved":
        raise Abgelehnt("device_pending" if row["status"] == "pending" else "device_blocked")
    return row["id"], row["firma_id"]


@dataclass
class Slots:
    """Laufende Upload-Lesevorgänge. Eine Event-Loop, daher ohne Lock."""

    instanz: int = 0
    firma: dict[str, int] = field(default_factory=dict)
    geraet: dict[str, int] = field(default_factory=dict)

    def belegen(self, gid: str, fid: str) -> None:
        if self.geraet.get(gid, 0) >= MAX_JE_GERAET or self.firma.get(fid, 0) >= MAX_JE_FIRMA:
            raise Abgelehnt("rate_limited", 5)
        if self.instanz >= MAX_INSTANZ:
            raise Abgelehnt("service_unavailable", 5)
        self.instanz += 1
        self.firma[fid] = self.firma.get(fid, 0) + 1
        self.geraet[gid] = self.geraet.get(gid, 0) + 1

    def freigeben(self, gid: str, fid: str) -> None:
        self.instanz -= 1
        for zaehler, schluessel in ((self.firma, fid), (self.geraet, gid)):
            zaehler[schluessel] -= 1
            if not zaehler[schluessel]:
                del zaehler[schluessel]
