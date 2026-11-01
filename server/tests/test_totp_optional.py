"""Zwei-Faktor optional (W2-SFIX Nachtrag, Christof-Entscheidung 2026-11-01): Standard-Anmeldearten password_totp +
password, „Später einrichten“ beim ersten Login, Konten mit TOTP brauchen weiter den Code, Hinweis in der Übersicht,
owner verlangt Zwei-Faktor über die Seite „Firma“ (nur password_totp).
"""

import json
from pathlib import Path

from fastapi.testclient import TestClient

from whisperloom_server import cli, db, firmen
from whisperloom_server.app import create_app
from whisperloom_server.config import Einstellungen

from .conftest import (
    BASIS,
    admin_client,
    csrf_aus,
    neue_einst,
    neue_firma,
    neuer_admin,
    passwort_login,
    sql,
    totp_code,
)

BEIDE = ("password_totp", "password")
HINWEIS_DE = "Zwei-Faktor ist nicht eingerichtet"
SITZUNG = "__Host-wl_sitzung"  # BASIS ist https


def _spaeter(c: TestClient, html: str):
    return c.post("/login/totp-spaeter", data={"csrf": csrf_aus(html)}, follow_redirects=False)


def test_standard_ohne_angabe_erlaubt_beide_arten(monkeypatch, tmp_path):
    monkeypatch.setenv("WL_DATA_DIR", str(tmp_path))
    monkeypatch.delenv("WL_AUTH_MODES", raising=False)
    einst = Einstellungen.aus_umgebung()
    assert einst.anmeldearten == BEIDE
    cli.init(einst)
    fid = neue_firma(einst, "neu")["id"]
    con = db.verbinden(einst.db_pfad)
    try:
        firma = con.execute("SELECT * FROM firma WHERE id = ?", (fid,)).fetchone()
        assert firma["anmeldearten"] == ""  # neue Firma ohne eigene Einschränkung → alle Arten der Instanz
        assert firmen.anmeldearten(firma, einst.anmeldearten) == BEIDE
    finally:
        con.close()


def test_spaeter_einrichten_beim_ersten_login(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        assert passwort_login(c, "anna").headers["location"] == "/login/totp-einrichten"
        html = c.get("/login/totp-einrichten").text
        assert 'action="/login/totp-spaeter"' in html and "Später einrichten" in html
        vorher = c.cookies.get(SITZUNG)
        r = _spaeter(c, html)
        assert r.status_code == 303 and r.headers["location"] == "/"
        assert c.cookies.get(SITZUNG) != vorher  # neue Sitzungs-ID
        seite = c.get("/")
        assert seite.status_code == 200 and HINWEIS_DE in seite.text and 'href="/admins#t-konto"' in seite.text
        assert tuple(sql(einst, "SELECT totp_enc IS NULL, totp_spaeter_ms > 0 FROM admin WHERE benutzer = 'anna'")[0]) == (1, 1)
        # Nächster Login: ohne Code und ohne Einrichtungsseite
        c.cookies.clear()
        assert passwort_login(c, "anna").headers["location"] == "/"
        # Einrichten später unter Admins → „Mein Konto“
        assert 'href="/login/totp-einrichten"' in c.get("/admins").text


def test_spaeter_ohne_recht_wird_abgelehnt(tmp_path, uhr):
    """Firma verlangt Zwei-Faktor (nur password_totp, bestehende Einstellung): kein Knopf, Server lehnt mit 403 ab."""
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a", anmeldearten="password_totp")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        assert passwort_login(c, "anna").headers["location"] == "/login/totp-einrichten"
        html = c.get("/login/totp-einrichten").text
        assert "/login/totp-spaeter" not in html
        assert _spaeter(c, html).status_code == 403
        assert c.get("/", follow_redirects=False).headers["location"] == "/login"
    assert sql(einst, "SELECT totp_spaeter_ms FROM admin WHERE benutzer = 'anna'")[0][0] == 0


def test_spaeter_braucht_csrf_und_einrichtungs_stufe(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        assert c.post("/login/totp-spaeter", data={"csrf": "x"}, follow_redirects=False).headers["location"] == "/login"
        passwort_login(c, "anna")
        assert c.post("/login/totp-spaeter", data={"csrf": "falsch"}, follow_redirects=False).status_code == 403
    assert sql(einst, "SELECT totp_spaeter_ms FROM admin WHERE benutzer = 'anna'")[0][0] == 0


def test_spaeter_mit_gleichem_rate_limit(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        passwort_login(c, "anna")
        html = c.get("/login/totp-einrichten").text
        for _ in range(5):
            c.post("/login/totp-einrichten", data={"code": "000000", "csrf": csrf_aus(html)})
        assert _spaeter(c, html).status_code == 429


def test_konto_mit_totp_braucht_weiter_den_code(tmp_path, uhr):
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a")
        neuer_admin(einst, f["id"], "anna", mit_totp=True)
        r = passwort_login(c, "anna")
        assert r.headers["location"] == "/login/totp"
        html = c.get("/login/totp").text
        # Überspringen aus der Code-Stufe geht nicht: keine volle Sitzung ohne Code
        assert _spaeter(c, html).headers["location"] == "/login"
        assert c.get("/", follow_redirects=False).headers["location"] == "/login"
        r = c.post("/login/totp", data={"code": totp_code(uhr), "csrf": csrf_aus(html)}, follow_redirects=False)
        assert r.headers["location"] == "/"
        assert HINWEIS_DE not in c.get("/").text


def test_owner_verlangt_zwei_faktor_ueber_firma(tmp_path, uhr):
    """Seite „Firma“: nur „Passwort und Zwei-Faktor“ → wer übersprungen hat, muss beim nächsten Login einrichten."""
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    app = create_app(einst)
    f = neue_firma(einst, "firma-a")
    with TestClient(app, base_url=BASIS) as c:
        neuer_admin(einst, f["id"], "anna", rolle="admin", mit_totp=False)
        passwort_login(c, "anna")
        assert _spaeter(c, c.get("/login/totp-einrichten").text).headers["location"] == "/"
        owner = admin_client(app, einst, uhr, f["id"], "olga")
        firma_html = owner.get("/firma").text
        assert "Zwei-Faktor für alle Admins verlangen" in firma_html
        r = owner.post("/firma", data={"csrf": csrf_aus(firma_html), "name": "Firma A", "key_modus": "proxy",
                                       "max_wartend": "50", "art:password_totp": "on"}, follow_redirects=False)
        assert r.headers["location"] == "/firma?ok=gespeichert"
        assert c.get("/", follow_redirects=False).headers["location"] == "/login"  # Sitzung ohne Faktor endet
        c.cookies.clear()
        assert passwort_login(c, "anna").headers["location"] == "/login/totp-einrichten"
        assert "/login/totp-spaeter" not in c.get("/login/totp-einrichten").text


def test_nur_password_ohne_einrichtungsseite(tmp_path, uhr):
    """Erlaubt die Firma nur `password`, gibt es kein Angebot zur Einrichtung beim Login (wie bisher)."""
    einst = neue_einst(tmp_path, uhr, anmeldearten=BEIDE)
    with TestClient(create_app(einst), base_url=BASIS) as c:
        f = neue_firma(einst, "firma-a", anmeldearten="password")
        neuer_admin(einst, f["id"], "anna", mit_totp=False)
        assert passwort_login(c, "anna").headers["location"] == "/"
        assert HINWEIS_DE in c.get("/").text


def test_texte_de_und_en():
    ordner = Path(firmen.__file__).parent / "texte"
    for sprache in ("de", "en"):
        texte = json.loads((ordner / f"{sprache}.json").read_text(encoding="utf-8"))
        for k in ("p.totp_spaeter", "p.totp_spaeter_text", "p.totp_hinweis", "p.totp_hinweis_link", "p.zwei_faktor_verlangen"):
            assert texte.get(k), (sprache, k)


def test_migration_fuegt_spalte_hinzu_ohne_firmen_zu_aendern(tmp_path, uhr):
    """Bestand auf Schema 2: Firmen-Anmeldearten bleiben, die neue Spalte startet bei 0."""
    pfad = tmp_path / "alt.db"
    con = db.verbinden(pfad)
    try:
        con.executescript("BEGIN;" + db.MIGRATIONEN[0] + "PRAGMA user_version = 1;COMMIT;")
        con.executescript("BEGIN;" + db.MIGRATIONEN[1] + "PRAGMA user_version = 2;COMMIT;")
        con.execute("INSERT INTO firma (id, slug, name, anmeldearten, angelegt_ms) VALUES ('f1', 'alt', 'Alt', "
                    "'password_totp', 0)")
        con.execute("INSERT INTO admin (id, firma_id, benutzer, rolle, angelegt_ms) VALUES ('a1', 'f1', 'alt', 'owner', 0)")
    finally:
        con.close()
    db.einrichten(pfad)
    con = db.verbinden(pfad)
    try:
        assert con.execute("PRAGMA user_version").fetchone()[0] == db.SCHEMA_VERSION
        assert con.execute("SELECT anmeldearten FROM firma").fetchone()[0] == "password_totp"
        assert con.execute("SELECT totp_spaeter_ms FROM admin").fetchone()[0] == 0
    finally:
        con.close()
