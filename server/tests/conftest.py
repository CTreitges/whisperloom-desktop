"""Gemeinsame Test-Bausteine: steuerbare Uhr, Instanz mit Anmeldearten, Firmen/Admins/Geräte anlegen, Login mit TOTP.

Basis: whisperloom-android feat/simple-portal server/simple-whisper/tests/conftest.py
Alle Keys, Codes, Passwörter und TOTP-Geheimnisse hier sind Fake-Werte.
"""

import importlib.util
import io
import re
import wave
from pathlib import Path

import pytest
from fastapi.routing import APIRoute
from fastapi.testclient import TestClient

from whisperloom_server import auth, cli, db, firmen, konten, krypto, schluessel, ssrf, totp
from whisperloom_server.app import create_app
from whisperloom_server.config import Einstellungen, lade_master
from whisperloom_server.krypto import Krypto

BASIS = "https://testserver"
PASSWORT = "test-passwort-nicht-echt-1"
FAKE_KEY = "test-key-not-real-1"
TOTP_GEHEIMNIS = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP"  # bekanntes Test-Geheimnis, nicht echt


class Uhr:
    def __init__(self):
        self.t = 1_800_000_000_000

    def __call__(self) -> int:
        return self.t

    def vor(self, ms: int) -> None:
        self.t += ms


@pytest.fixture
def uhr():
    return Uhr()


def neue_einst(tmp_path: Path, uhr: Uhr, **kw) -> Einstellungen:
    e = Einstellungen(daten=tmp_path / "daten", basis_url=kw.pop("basis_url", BASIS), uhr=uhr, **kw)
    cli.init(e)
    return e


@pytest.fixture
def einst(tmp_path: Path, uhr: Uhr) -> Einstellungen:
    return neue_einst(tmp_path, uhr)


@pytest.fixture
def app(einst):
    return create_app(einst)


@pytest.fixture
def client(app):
    with TestClient(app, base_url=BASIS) as c:
        yield c


def kry(einst: Einstellungen) -> Krypto:
    return Krypto(lade_master(einst))


def sql(einst: Einstellungen, abfrage: str, werte: tuple = ()):
    con = db.verbinden(einst.db_pfad)
    try:
        return con.execute(abfrage, werte).fetchall()
    finally:
        con.close()


def neue_firma(einst: Einstellungen, slug: str, name: str | None = None, **felder) -> dict:
    con = db.verbinden(einst.db_pfad)
    try:
        fid, code = firmen.anlegen(con, kry(einst), slug, name or slug.title(), einst.uhr())
        for k, v in felder.items():
            con.execute(f"UPDATE firma SET {k} = ? WHERE id = ?", (v, fid))  # noqa: S608 (feste Spalten aus Tests)
    finally:
        con.close()
    return {"id": fid, "code": krypto.code_anzeigen(code), "slug": slug}


def neuer_admin(einst: Einstellungen, firma_id: str | None, benutzer: str, rolle: str = "owner",
                mit_totp: bool = True) -> dict:
    con = db.verbinden(einst.db_pfad)
    try:
        aid = konten.anlegen(con, firma_id, benutzer, rolle, einst.uhr(), passwort=PASSWORT)
        if mit_totp:
            auth.totp_speichern(con, kry(einst), aid, TOTP_GEHEIMNIS)
    finally:
        con.close()
    return {"id": aid, "benutzer": benutzer, "totp": mit_totp}


def csrf_aus(html: str) -> str:
    return re.search(r'name="csrf" value="([^"]+)"', html).group(1)


def seiten_csrf(client: TestClient, pfad: str = "/") -> str:
    r = client.get(pfad)
    assert r.status_code == 200, (pfad, r.status_code)
    return csrf_aus(r.text)


def passwort_login(client: TestClient, benutzer: str, passwort: str = PASSWORT):
    r = client.get("/login")
    return client.post("/login", data={"benutzername": benutzer, "passwort": passwort, "csrf": csrf_aus(r.text)},
                       follow_redirects=False)


def totp_code(uhr: Uhr) -> str:
    uhr.vor(31_000)  # neuer Zeitschritt, sonst greift der Schutz gegen Wiederverwendung
    return totp.code_fuer(TOTP_GEHEIMNIS, uhr())


def anmelden(client: TestClient, admin: dict, uhr: Uhr) -> None:
    r = passwort_login(client, admin["benutzer"])
    assert r.status_code == 303, r.text
    if r.headers["location"] == "/login/totp":
        csrf = seiten_csrf(client, "/login/totp")
        r = client.post("/login/totp", data={"code": totp_code(uhr), "csrf": csrf}, follow_redirects=False)
        assert r.status_code == 303 and r.headers["location"] == "/", r.text
    else:
        assert r.headers["location"] == "/", r.headers["location"]


def admin_client(app, einst: Einstellungen, uhr: Uhr, firma_id: str | None, benutzer: str, rolle: str = "owner") -> TestClient:
    c = TestClient(app, base_url=BASIS)
    anmelden(c, neuer_admin(einst, firma_id, benutzer, rolle), uhr)
    return c


def enroll(client: TestClient, code: str, name: str = "PC-Test-1", platform: str = "windows") -> dict:
    r = client.post("/api/v1/enroll", json={"code": code, "deviceName": name, "platform": platform, "appVersion": "1.0.0"})
    assert r.status_code == 201, r.text
    return r.json()


def bearer(token: str) -> dict:
    return {"Authorization": f"Bearer {token}"}


def geraet_setzen(einst: Einstellungen, gid: str, **werte) -> None:
    con = db.verbinden(einst.db_pfad)
    try:
        for k, v in werte.items():
            con.execute(f"UPDATE geraet SET {k} = ? WHERE id = ?", (v, gid))  # noqa: S608 (feste Spalten aus Tests)
    finally:
        con.close()


def freies_geraet(client: TestClient, einst: Einstellungen, code: str, **werte) -> dict:
    g = enroll(client, code)
    geraet_setzen(einst, g["deviceId"], status="approved", **werte)
    return g


def alle_routen(routen) -> list[APIRoute]:
    """APIRoutes rekursiv (FastAPI 0.16x legt eingebundene Router als _IncludedRouter ab, nicht flach)."""
    gefunden = []
    for r in routen:
        if isinstance(r, APIRoute):
            gefunden.append(r)
        elif getattr(r, "original_router", None) is not None:
            gefunden += alle_routen(r.original_router.routes)
    return gefunden


# --- Proxy (W2-SRV2): Fake-Anbieter auf 127.0.0.1, Instanz mit Freigabe für Loopback (WL_UPSTREAM_ALLOW) ---

_FAKE = Path(__file__).resolve().parents[1] / "tools" / "fake_upstream.py"
_spec = importlib.util.spec_from_file_location("fake_upstream", _FAKE)
fake_upstream = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(fake_upstream)
FakeUpstream = fake_upstream.FakeUpstream
FAKE_KEY_2 = "test-key-not-real-2"
LOOPBACK = ssrf.freigaben_lesen("127.0.0.0/8")


def wav(sekunden: float = 1.0, rate: int = 16_000) -> bytes:
    puffer = io.BytesIO()
    with wave.open(puffer, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(rate)
        w.writeframes(b"\x00\x00" * int(sekunden * rate))
    return puffer.getvalue()


@pytest.fixture
def pein(tmp_path: Path, uhr: Uhr) -> Einstellungen:
    return neue_einst(tmp_path, uhr, upstream_erlaubt=LOOPBACK)


@pytest.fixture
def papp(pein):
    return create_app(pein)


@pytest.fixture
def pclient(papp):
    with TestClient(papp, base_url=BASIS) as c:
        yield c


@pytest.fixture
def fake():
    s = FakeUpstream(FAKE_KEY, "haupt").starten()
    yield s
    s.stoppen()


@pytest.fixture
def fake2():
    s = FakeUpstream(FAKE_KEY_2, "ersatz").starten()
    yield s
    s.stoppen()


def zugang(einst: Einstellungen, firma_id: str, name: str, anbieter: str, basis_url: str, key: str = FAKE_KEY,
           modell: str = "") -> None:
    con = db.verbinden(einst.db_pfad)
    try:
        schluessel.speichern(con, kry(einst), firma_id, name, anbieter, basis_url, key, 0, modell=modell)
    finally:
        con.close()


def proxy_geraet(client: TestClient, einst: Einstellungen, slug: str = "muster", **firma) -> tuple[dict, dict]:
    f = neue_firma(einst, slug, **{"key_modus": "proxy", **firma})
    return f, freies_geraet(client, einst, f["code"])


def stt(client: TestClient, token: str, daten: bytes | None = None, **felder):
    werte = {"model": "whisper-x", "language": "de", "response_format": "json", **felder}
    return client.post("/api/v1/proxy/audio/transcriptions", headers=bearer(token),
                       files={"file": ("audio.wav", wav() if daten is None else daten, "audio/wav")}, data=werte)


def chat(client: TestClient, token: str, text: str = "hallo welt", **extra):
    body = {"model": "text-x", "messages": [{"role": "system", "content": "Glätte den Text."},
                                            {"role": "user", "content": text}], "temperature": 0, **extra}
    return client.post("/api/v1/proxy/chat/completions", headers=bearer(token), json=body)
