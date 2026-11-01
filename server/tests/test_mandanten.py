"""Mandanten-Trennung: Ein owner von Firma A liest und ändert nie Geräte, Gruppen, Keys, Admins oder Richtlinie von Firma B.

Jede POST-Route des Portals ist parametrisiert (mit den IDs von Firma B, wo die Route eine ID nimmt); danach muss jede
Zeile von Firma B unverändert sein. Ein eigener Test stellt sicher, dass keine Portal-Route in der Liste fehlt.
"""

import pytest

from whisperloom_server import db, richtlinie, schluessel

from .conftest import FAKE_KEY, admin_client, alle_routen, enroll, kry, neue_firma, neuer_admin, seiten_csrf, sql

TABELLEN = ["firma", "gruppe", "firmencode", "admin", "geraet", "richtlinie", "schluessel", "einladung"]

# (Pfad-Vorlage, Formularfelder). {g} Gerät B, {gr} Gruppe B, {a} Admin B, {k} Key-Name, {f} Firma B
ROUTEN = [
    *[(f"/geraete/{{g}}/{a}", {"name": "Übernommen", "gruppe": ""}) for a in
      ("freigeben", "sperren", "entsperren", "loeschen", "umbenennen", "gruppe")],
    *[(f"/gruppen/{{gr}}/{a}", {"name": "Übernommen"}) for a in ("umbenennen", "loeschen", "code", "code_entfernen")],
    *[(f"/admins/{{a}}/{x}", {"rolle": "admin"}) for x in
      ("einladung", "sperren", "entsperren", "loeschen", "rolle", "totp_zuruecksetzen")],
    ("/schluessel/{k}/entfernen", {}),
    ("/richtlinie", {"gruppe": "{gr}", "s:refine.stage": "fixed", "w:refine.stage": "summarize"}),
    ("/gruppen", {"name": "Neu"}),
    ("/schluessel", {"name": "stt", "anbieter": "groq", "key": "test-key-not-real-9"}),
    ("/admins", {"benutzer": "neu-in-a", "rolle": "admin"}),
    ("/firma", {"name": "A neu", "key_modus": "distribute", "art:password_totp": "on", "max_wartend": "5"}),
    ("/firma/code", {}),
    ("/konto/passwort", {"alt": "x", "neu": "y", "neu2": "y"}),
    ("/konto/totp-aus", {}),
    ("/instanz/firmen/{f}/sperren", {}),
    ("/instanz/firmen/{f}/oeffnen", {}),
    ("/instanz/firmen", {"slug": "neu", "name": "Neu"}),
    ("/logout", {}),
]
# Routen, die keine Firmendaten berühren (vor dem Login) — eigene Tests in test_anmeldearten/test_csrf.
OHNE_FIRMA = {"/login", "/login/totp", "/login/totp-einrichten", "/einladung/{token}"}


def _stand(einst, fid: str) -> dict:
    con = db.verbinden(einst.db_pfad)
    try:
        stand = {}
        for t in TABELLEN:
            if t == "firma":
                q = "SELECT * FROM firma WHERE id = ?"
            elif t == "einladung":
                q = "SELECT e.* FROM einladung e JOIN admin a ON a.id = e.admin_id WHERE a.firma_id = ?"
            else:
                q = f"SELECT * FROM {t} WHERE firma_id = ?"  # noqa: S608
            stand[t] = sorted(tuple(r) for r in con.execute(q, (fid,)).fetchall())
        return stand
    finally:
        con.close()


@pytest.fixture
def zwei(app, einst, uhr, client):
    a = neue_firma(einst, "firma-a", "Firma A")
    b = neue_firma(einst, "firma-b", "Firma B")
    con = db.verbinden(einst.db_pfad)
    k = kry(einst)
    from whisperloom_server import krypto

    gid_b = krypto.kurz_id("g_")
    con.execute("INSERT INTO gruppe (id, firma_id, name, angelegt_ms) VALUES (?, ?, 'Geheimgruppe B', 0)", (gid_b, b["id"]))
    richtlinie.setzen(con, b["id"], "", "refine.stage", "fixed", "polish")
    richtlinie.setzen(con, b["id"], gid_b, "history.enabled", "fixed", False)
    schluessel.speichern(con, k, b["id"], "stt", "groq", "", "test-key-not-real-b-7777", 0)
    schluessel.speichern(con, k, b["id"], "mistral", "mistral", "", "test-key-not-real-b-8888", 0)
    schluessel.speichern(con, k, a["id"], "stt", "groq", "", FAKE_KEY, 0)
    con.close()
    from whisperloom_server import firmen

    con = db.verbinden(einst.db_pfad)
    firmen.code_setzen(con, k, b["id"], gid_b, 0)
    con.close()
    g_b = enroll(client, b["code"], name="Geheimgerät B")
    admin_b = neuer_admin(einst, b["id"], "bernd-b")
    c = admin_client(app, einst, uhr, a["id"], "anna-a")
    return {"c": c, "a": a, "b": b, "ids": {"g": g_b["deviceId"], "gr": gid_b, "a": admin_b["id"], "k": "mistral", "f": b["id"]}}


@pytest.mark.parametrize("vorlage,felder", ROUTEN, ids=[r[0] for r in ROUTEN])
def test_post_aendert_firma_b_nie(zwei, einst, vorlage, felder):
    c, b, ids = zwei["c"], zwei["b"], zwei["ids"]
    vorher = _stand(einst, b["id"])
    csrf = seiten_csrf(c, "/gruppen")
    daten = {k: v.format(**ids) for k, v in felder.items()}
    r = c.post(vorlage.format(**ids), data={**daten, "csrf": csrf}, follow_redirects=False)
    if "{" in vorlage:
        assert r.status_code in (403, 404), (vorlage, r.status_code)
    assert _stand(einst, b["id"]) == vorher, vorlage


def test_jede_portal_post_route_ist_abgedeckt(app):
    abgedeckt = {v.split("{")[0].rstrip("/") for v, _ in ROUTEN}
    routen = alle_routen(app.routes)
    assert len(routen) > 20
    for route in routen:
        if "POST" in route.methods and not route.path.startswith("/api/"):
            if route.path in OHNE_FIRMA:
                continue
            basis = route.path.split("{")[0].rstrip("/")
            assert basis in abgedeckt, f"Mandanten-Test fehlt für {route.path}"


@pytest.mark.parametrize("pfad", ["/", "/geraete", "/gruppen", "/richtlinie", "/schluessel", "/admins", "/firma"])
def test_get_zeigt_nichts_von_firma_b(zwei, pfad):
    html = zwei["c"].get(pfad).text
    for geheim in ("Firma B", "Geheimgerät B", "Geheimgruppe B", "bernd-b", "7777", zwei["b"]["code"], zwei["ids"]["g"]):
        assert geheim not in html, (pfad, geheim)


def test_richtlinie_einer_fremden_gruppe_nicht_lesbar(zwei):
    assert zwei["c"].get(f"/richtlinie?gruppe={zwei['ids']['gr']}").status_code == 404


def test_eigenes_geraet_nicht_in_fremde_gruppe(zwei, einst, client):
    g_a = enroll(client, zwei["a"]["code"])
    csrf = seiten_csrf(zwei["c"], "/geraete")
    r = zwei["c"].post(f"/geraete/{g_a['deviceId']}/gruppe", data={"csrf": csrf, "gruppe": zwei["ids"]["gr"]})
    assert r.status_code == 404
    assert sql(einst, "SELECT gruppe_id FROM geraet WHERE id = ?", (g_a["deviceId"],))[0][0] is None


def test_key_von_b_bleibt_beim_entfernen_in_a(zwei, einst):
    csrf = seiten_csrf(zwei["c"], "/schluessel")
    assert zwei["c"].post("/schluessel/stt/entfernen", data={"csrf": csrf}, follow_redirects=False).status_code == 303
    assert sql(einst, "SELECT COUNT(*) FROM schluessel WHERE firma_id = ? AND zugang = 'stt'", (zwei["b"]["id"],))[0][0] == 1
    assert sql(einst, "SELECT COUNT(*) FROM schluessel WHERE firma_id = ?", (zwei["a"]["id"],))[0][0] == 0


def test_instanz_admin_sieht_keine_firmendaten(app, einst, uhr, zwei):
    c = admin_client(app, einst, uhr, None, "root-admin", rolle="instance")
    r = c.get("/", follow_redirects=False)
    assert r.status_code == 303 and r.headers["location"] == "/instanz"
    for pfad in ("/geraete", "/schluessel", "/richtlinie", "/admins"):
        assert c.get(pfad, follow_redirects=False).headers["location"] == "/instanz"
    html = c.get("/instanz").text
    assert "Firma B" in html and "Geheimgerät B" not in html and "7777" not in html
