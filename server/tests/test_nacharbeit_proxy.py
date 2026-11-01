"""Regressionstests Nacharbeit W2-SRV2 (Prüfrunde 1 des Orchestrators, W2/W2-SRV2/review-orchestrator.md):
1 Slowloris auf die Upload-Slots, 3 Budget-Reservierung laufender Anfragen, 4 Metadaten-IPs trotz WL_UPSTREAM_ALLOW.
"""

import asyncio

import httpx
import pytest

from whisperloom_server import sicherheit, ssrf
from whisperloom_server.app import create_app

from .conftest import FAKE_KEY, bearer, proxy_geraet, sql, wav, zugang

PFAD = "/api/v1/proxy/audio/transcriptions"


def _scope(token: str | None, laenge: int = 1_000_000) -> dict:
    kopf = [(b"content-length", str(laenge).encode()), (b"content-type", b"multipart/form-data; boundary=x"),
            (b"host", b"testserver")]
    if token is not None:
        kopf.append((b"authorization", f"Bearer {token}".encode()))
    return {"type": "http", "asgi": {"version": "3.0"}, "http_version": "1.1", "method": "POST", "scheme": "https",
            "path": PFAD, "raw_path": PFAD.encode(), "root_path": "", "query_string": b"", "headers": kopf,
            "client": ("203.0.113.7", 40000), "server": ("testserver", 443)}


async def _anfrage(app, scope: dict, empfangen) -> tuple[int, bytes]:
    gesendet: list[dict] = []

    async def senden(nachricht):
        gesendet.append(nachricht)

    await asyncio.wait_for(app(scope, empfangen, senden), timeout=5)
    status = next(n["status"] for n in gesendet if n["type"] == "http.response.start")
    return status, b"".join(n.get("body", b"") for n in gesendet if n["type"] == "http.response.body")


def _middleware(app) -> sicherheit.SicherheitsMiddleware:
    """Die Instanz der Middleware im gebauten Stack (für den Blick auf die Slot-Zähler)."""
    if app.middleware_stack is None:
        app.middleware_stack = app.build_middleware_stack()
    schicht = app.middleware_stack
    while not isinstance(schicht, sicherheit.SicherheitsMiddleware):
        schicht = schicht.app
    return schicht


async def _haengt():
    await asyncio.sleep(3600)
    return {"type": "http.disconnect"}


# --- 1: Slowloris ---


def test_ohne_gueltiges_token_wird_kein_body_gelesen(pein):
    app = create_app(pein)
    gelesen = []

    async def empfangen():
        gelesen.append(1)
        return await _haengt()

    for token in (None, "wld_" + "x" * 43):
        status, body = asyncio.run(_anfrage(app, _scope(token), empfangen))
        assert status == 401 and b"device_unknown" in body
    assert gelesen == []  # kein Byte des Bodies angefasst, kein Slot belegt
    assert _middleware(app).slots.instanz == 0


def test_haengender_upload_endet_am_lese_zeitlimit_und_gibt_den_slot_frei(pein, pclient, monkeypatch):
    monkeypatch.setattr(sicherheit, "LESE_LEERLAUF_S", 0.3)
    _f, g = proxy_geraet(pclient, pein)
    app = create_app(pein)
    teile = [{"type": "http.request", "body": b"--x\r\n", "more_body": True}]

    async def empfangen():
        if teile:
            return teile.pop()
        return await _haengt()

    status, body = asyncio.run(_anfrage(app, _scope(g["deviceToken"]), empfangen))
    assert status == 400 and b"invalid_request" in body
    m = _middleware(app)
    assert m.slots.instanz == 0 and m.slots.geraet == {} and m.slots.firma == {}


def test_slots_je_geraet_und_firma(pein, pclient, monkeypatch):
    _f, g = proxy_geraet(pclient, pein)
    app = create_app(pein)
    m = _middleware(app)
    from whisperloom_server import vorab

    monkeypatch.setattr(vorab, "MAX_JE_GERAET", 1)
    m.slots.geraet[g["deviceId"]] = 1  # ein Upload dieses Geräts liest noch
    status, body = asyncio.run(_anfrage(app, _scope(g["deviceToken"]), _haengt))
    assert status == 429 and b"rate_limited" in body
    m.slots.geraet.clear()
    monkeypatch.setattr(vorab, "MAX_JE_FIRMA", 0)
    status, _ = asyncio.run(_anfrage(app, _scope(g["deviceToken"]), _haengt))
    assert status == 429


def test_wartendes_oder_gesperrtes_geraet_belegt_keinen_slot(pein, pclient):
    from .conftest import enroll, geraet_setzen, neue_firma

    f = neue_firma(pein, "wartend", key_modus="proxy")
    g = enroll(pclient, f["code"])
    app = create_app(pein)
    status, body = asyncio.run(_anfrage(app, _scope(g["deviceToken"]), _haengt))
    assert status == 403 and b"device_pending" in body
    geraet_setzen(pein, g["deviceId"], status="blocked")
    status, body = asyncio.run(_anfrage(app, _scope(g["deviceToken"]), _haengt))
    assert status == 403 and b"device_blocked" in body
    assert _middleware(app).slots.instanz == 0


# --- 3: Budget-Reservierung ---


def test_gleichzeitige_anfragen_reservieren_das_budget(pein, pclient, papp, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    sql_budget = "INSERT INTO budget (firma_id, ebene, ziel_id, max_anfragen) VALUES (?, 'firma', '', 2)"
    from whisperloom_server import db

    con = db.verbinden(pein.db_pfad)
    con.execute(sql_budget, (f["id"],))
    con.close()
    fake.modus, fake.warte_s = "langsam", 1.0
    audio = wav(0.5)

    async def los():
        transport = httpx.ASGITransport(app=papp)
        async with httpx.AsyncClient(transport=transport, base_url="https://testserver") as c:
            return await asyncio.gather(*[c.post(PFAD, headers=bearer(g["deviceToken"]), data={"model": "m"},
                                                 files={"file": ("a.wav", audio, "audio/wav")}) for _ in range(5)])

    antworten = asyncio.run(los())
    status = sorted(r.status_code for r in antworten)
    assert status == [200, 200, 403, 403, 403], status
    assert len(fake.anfragen) == 2
    assert sql(pein, "SELECT anfragen FROM nutzung")[0][0] == 2


def test_minuten_reservierung(pein, pclient, papp, fake):
    f, g = proxy_geraet(pclient, pein)
    zugang(pein, f["id"], "stt", "custom", fake.basis + "/v1")
    from whisperloom_server import db

    con = db.verbinden(pein.db_pfad)
    con.execute("INSERT INTO budget (firma_id, ebene, ziel_id, max_audio_min) VALUES (?, 'geraet', ?, 1)",
                (f["id"], g["deviceId"]))
    con.close()
    fake.modus, fake.warte_s = "langsam", 1.0
    audio = wav(40.0)  # wie nacheinander: 0 s und 40 s < 1 min laufen an, die dritte (80 s reserviert) nicht

    async def los():
        transport = httpx.ASGITransport(app=papp)
        async with httpx.AsyncClient(transport=transport, base_url="https://testserver") as c:
            return await asyncio.gather(*[c.post(PFAD, headers=bearer(g["deviceToken"]), data={"model": "m"},
                                                 files={"file": ("a.wav", audio, "audio/wav")}) for _ in range(3)])

    assert sorted(r.status_code for r in asyncio.run(los())) == [200, 200, 403]


# --- 4: Metadaten-IPs ---


@pytest.mark.parametrize("adresse", ["169.254.169.254", "fd00:ec2::254", "100.100.100.200", "169.254.170.2",
                                     "::ffff:169.254.169.254"])
def test_metadaten_trotz_freigabe_gesperrt(adresse):
    alles = ssrf.freigaben_lesen("0.0.0.0/0, ::/0")
    with pytest.raises(ssrf.ZielVerboten):
        ssrf.pruefen("http://meta.example/latest", alles, lambda _h, _p: [adresse])
    assert ssrf.pruefen("http://intern.example/v1", alles, lambda _h, _p: ["169.254.10.10"])  # übriges link-local frei


def test_fake_key_bleibt_fake():
    assert FAKE_KEY.startswith("test-key-not-real")
