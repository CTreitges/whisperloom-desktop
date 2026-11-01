"""Review M-5 (W2-FIXS): Login-Sperre je (Benutzer, IP bzw. /64), weiche globale Grenze, Erfolge zählen nicht, CLI."""

from fastapi.testclient import TestClient

from whisperloom_server import auth, cli, db

from .conftest import BASIS, PASSWORT, neue_firma, neuer_admin, passwort_login, sql

FALSCH = "falsch-falsch-falsch"
STUNDE = 3_600_000


def _von(app, ip: str) -> TestClient:
    return TestClient(app, base_url=BASIS, client=(ip, 1))


def test_sonde_kontosperre_durch_fremde(einst, app, uhr):
    """Sonde aus dem Security-Review: 5 falsche Passwörter eines Dritten sperren den echten Admin nicht aus."""
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    angreifer = _von(app, "203.0.113.7")
    for _ in range(5):
        assert passwort_login(angreifer, "chef", FALSCH).status_code == 200
    assert passwort_login(angreifer, "chef", PASSWORT).status_code == 429  # der Angreifer selbst ist gesperrt
    r = passwort_login(_von(app, "198.51.100.20"), "chef", PASSWORT)
    assert (r.status_code, r.headers.get("location")) == (303, "/login/totp")


def test_sperre_gilt_fuer_ganzes_ipv6_64(einst, app):
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    for i in range(5):
        passwort_login(_von(app, f"2001:db8:1:2::{i + 1:x}"), "chef", FALSCH)
    assert passwort_login(_von(app, "2001:db8:1:2::ff"), "chef", PASSWORT).status_code == 429  # gleiches /64
    assert passwort_login(_von(app, "2001:db8:1:3::1"), "chef", PASSWORT).status_code == 303  # anderes /64


def test_sperre_endet_nach_15_minuten(einst, app, uhr):
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    c = _von(app, "203.0.113.7")
    for _ in range(5):
        passwort_login(c, "chef", FALSCH)
    assert passwort_login(c, "chef", PASSWORT).status_code == 429
    uhr.vor(15 * 60_000 + 1000)
    assert passwort_login(c, "chef", PASSWORT).status_code == 303


def test_erfolgreiche_logins_zaehlen_nicht(einst, app):
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    c = _von(app, "203.0.113.7")
    for _ in range(4):
        passwort_login(c, "chef", FALSCH)
    assert passwort_login(c, "chef", PASSWORT).status_code == 303
    c.cookies.clear()
    for _ in range(4):  # nach dem Erfolg beginnt die Zählung neu: weitere 4 Tippfehler sperren noch nicht
        assert passwort_login(c, "chef", FALSCH).status_code == 200
    assert passwort_login(c, "chef", PASSWORT).status_code == 303
    for _ in range(8):  # viele Erfolge hintereinander lösen keine Sperre aus
        c.cookies.clear()
        assert passwort_login(c, "chef", PASSWORT).status_code == 303


def test_weiche_globale_grenze(einst, app, uhr):
    """Ab 50 Fehlversuchen je Stunde: jede IP nur noch ein Versuch je Stunde, eine neue IP darf weiter einmal."""
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    for i in range(auth.GLOBAL_MAX):
        passwort_login(_von(app, f"198.51.100.{i}"), "chef", FALSCH)
    neu = _von(app, "198.51.100.200")
    assert passwort_login(neu, "chef", FALSCH).status_code == 200  # erster Versuch einer neuen IP ist frei
    assert passwort_login(neu, "chef", PASSWORT).status_code == 429  # zweiter nicht mehr
    assert passwort_login(_von(app, "198.51.100.3"), "chef", PASSWORT).status_code == 429  # schon gezählte IP
    assert passwort_login(_von(app, "198.51.100.201"), "chef", PASSWORT).status_code == 303  # echter Admin
    uhr.vor(STUNDE + 1000)
    assert passwort_login(_von(app, "198.51.100.3"), "chef", PASSWORT).status_code == 303


def test_unbekannter_name_wie_bekannter(einst, app):
    """Review N3/R5 bleibt: unbekannte Namen laufen durch dieselbe Zählung, Antworten sind gleich."""
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    c = _von(app, "203.0.113.7")
    status = {}
    for name in ("chef", "gibt-es-nicht"):
        for _ in range(5):
            passwort_login(c, name, FALSCH)
        status[name] = passwort_login(c, name, FALSCH).status_code
    assert status == {"chef": 429, "gibt-es-nicht": 429}


def test_gleichzeitige_versuche_einer_ip_hoechstens_fuenf(einst, monkeypatch):
    """Review-Befund: Prüfen und Eintragen vor argon2 – parallele Fehlversuche einer IP sehen sich gegenseitig."""
    import threading
    import time

    from .conftest import kry

    langsam = auth.passwort_pruefen
    monkeypatch.setattr(auth, "passwort_pruefen", lambda h, p: (time.sleep(0.2), langsam(h, p))[1])
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    k, start, ergebnisse = kry(einst), threading.Barrier(10), []

    def versuch():
        con = db.verbinden(einst.db_pfad)
        try:
            start.wait()
            ergebnisse.append(auth.anmelden(con, k, "chef", FALSCH, "203.0.113.7", einst.uhr())[0])
        finally:
            con.close()

    faeden = [threading.Thread(target=versuch) for _ in range(10)]
    for t in faeden:
        t.start()
    for t in faeden:
        t.join()
    assert sorted(ergebnisse) == ["falsch"] * 5 + ["gesperrt"] * 5


def test_totp_fehlversuche_ueber_neue_logins_sperren_konto(einst, app, uhr):
    """Review-Befund: ein richtiges Passwort setzt die TOTP-Fehlversuche nicht zurück (sonst 4 je Login, endlos)."""
    from .conftest import seiten_csrf

    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    c = _von(app, "203.0.113.7")
    for runde in range(5):
        c.cookies.clear()
        assert passwort_login(c, "chef", PASSWORT).headers.get("location") == "/login/totp", runde
        c.post("/login/totp", data={"code": "000000", "csrf": seiten_csrf(c, "/login/totp")})
    assert sql(einst, "SELECT gesperrt_bis_ms > 0 FROM admin WHERE benutzer = 'chef'")[0][0] == 1
    assert passwort_login(_von(app, "198.51.100.20"), "chef", PASSWORT).status_code == 429


def test_weiche_grenze_vergisst_tippfehler_vor_erfolg(einst, app):
    """Review-Befund: Ein Tippfehler des echten Admins vor seinem Erfolg zählt unter Angriff nicht gegen seine IP."""
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    echt = _von(app, "198.51.100.250")
    passwort_login(echt, "chef", FALSCH)
    assert passwort_login(echt, "chef", PASSWORT).status_code == 303
    for i in range(auth.GLOBAL_MAX):
        passwort_login(_von(app, f"198.51.100.{i}"), "chef", FALSCH)
    echt.cookies.clear()
    assert passwort_login(echt, "chef", PASSWORT).status_code == 303


def test_cli_admin_unlock_hebt_sperre_auf(einst, app, capsys):
    f = neue_firma(einst, "muster")
    neuer_admin(einst, f["id"], "chef")
    c = _von(app, "203.0.113.7")
    for _ in range(5):
        passwort_login(c, "chef", FALSCH)
    con = db.verbinden(einst.db_pfad)
    try:
        con.execute("UPDATE admin SET gesperrt_bis_ms = ? WHERE benutzer = 'chef'", (einst.uhr() + STUNDE,))  # TOTP-Sperre
    finally:
        con.close()
    assert passwort_login(c, "chef", PASSWORT).status_code == 429
    assert cli.main(["admin-unlock", "Chef"], einst) == 0
    assert passwort_login(c, "chef", PASSWORT).status_code == 303
    assert sql(einst, "SELECT COUNT(*) FROM audit WHERE aktion = 'admin_entsperrt'")[0][0] == 1
    assert cli.main(["admin-unlock", "niemand"], einst) == 1
