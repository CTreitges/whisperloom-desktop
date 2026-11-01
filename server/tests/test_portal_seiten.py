"""Alle Portal-Seiten rendern für owner und admin, DE und EN; Richtlinie zeigt Klartext-Namen statt Pfaden."""

import pytest

from .conftest import admin_client, neue_firma

SEITEN = ["/", "/geraete", "/gruppen", "/richtlinie", "/schluessel", "/nutzung", "/budget", "/admins", "/firma"]


@pytest.mark.parametrize("pfad", SEITEN)
@pytest.mark.parametrize("sprache", ["de", "en"])
def test_seite_rendert(app, einst, uhr, pfad, sprache):
    f = neue_firma(einst, "muster", "Muster GmbH")
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    c.cookies.set("wl_sprache", sprache)
    r = c.get(pfad)
    assert r.status_code == 200, r.text
    assert f'<html lang="{sprache}">' in r.text
    assert "Muster GmbH" in r.text


def test_richtlinie_klartext_und_gruppiert(app, einst, uhr):
    f = neue_firma(einst, "muster")
    c = admin_client(app, einst, uhr, f["id"], "owner-a")
    html = c.get("/richtlinie").text
    for name in ("Erkennung", "Textverbesserung", "Tastenkürzel", "Stufe beim Diktat", "Verlauf speichern"):
        assert name in html
    # Pfade nur als Formularnamen, nicht als sichtbarer Text
    assert ">refine.stage<" not in html and "s.refine.stage" not in html
    c.cookies.set("wl_sprache", "en")
    assert "Stage when dictating" in c.get("/richtlinie").text


def test_ohne_login_weiter_zum_login(client):
    r = client.get("/geraete", follow_redirects=False)
    assert r.status_code == 303 and r.headers["location"] == "/login"


def test_admin_darf_keine_owner_aktionen(app, einst, uhr):
    f = neue_firma(einst, "muster")
    c = admin_client(app, einst, uhr, f["id"], "admin-a", rolle="admin")
    assert c.get("/firma").status_code == 200  # ansehen ja
    from .conftest import seiten_csrf

    csrf = seiten_csrf(c, "/firma")
    assert c.post("/firma/code", data={"csrf": csrf}).status_code == 403
    assert c.post("/admins", data={"csrf": csrf, "benutzer": "neu-x", "rolle": "owner"}).status_code == 403
